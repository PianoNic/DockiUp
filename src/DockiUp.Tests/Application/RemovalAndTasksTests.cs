using Docker.DotNet;
using DockiUp.Application.Commands;
using DockiUp.Application.Deployments;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Application;

public class RemovalAndTasksTests
{
    private static ProjectInfo Project(string name, Guid? nodeId = null) => new()
    {
        ProjectName = name,
        DockerProjectName = name,
        ProjectOrigin = ProjectOriginType.Git,
        ProjectPath = $"/p/{name}",
        ComposePath = $"/p/{name}/docker-compose.yml",
        ProjectUpdateMethod = ProjectUpdateMethod.Webhook,
        NodeId = nodeId,
    };

    private static (Mock<IDockerService> docker, Mock<IDockerServiceResolver> resolver) Docker()
    {
        var docker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<Guid?>())).Returns(docker.Object);
        return (docker, resolver);
    }

    [Fact]
    public async Task RemoveProject_Managed_DownsProject_DeletesFiles_AndForgetsRecordAndHistory()
    {
        var db = TestDb.Create();
        var node = Guid.NewGuid();
        var project = Project("app", node);
        db.ProjectInfo.Add(project);
        db.Deployments.Add(new Deployment { ProjectId = project.Id, Trigger = DeploymentTrigger.Manual });
        db.SaveChanges();
        var (docker, resolver) = Docker();

        await new RemoveProjectCommandHandler(resolver.Object, db, new Mock<IActivityLogger>().Object)
            .Handle(new RemoveProjectCommand(project.Id, null, RemoveVolumes: true), CancellationToken.None);

        resolver.Verify(r => r.Resolve(node)); // on the project's own node
        docker.Verify(d => d.RemoveProjectAsync("app", true));
        docker.Verify(d => d.DeleteProjectFilesAsync("/p/app"));
        Assert.False(await db.ProjectInfo.AnyAsync());
        Assert.False(await db.Deployments.AnyAsync());
    }

    [Fact]
    public async Task RemoveProject_Unmanaged_OnlyDownsIt_OnTheGivenNode_KeepingVolumes()
    {
        var db = TestDb.Create();
        var node = Guid.NewGuid();
        var (docker, resolver) = Docker();

        await new RemoveProjectCommandHandler(resolver.Object, db, new Mock<IActivityLogger>().Object)
            .Handle(new RemoveProjectCommand(null, "legacy", node), CancellationToken.None);

        resolver.Verify(r => r.Resolve(node));
        docker.Verify(d => d.RemoveProjectAsync("legacy", false));
        docker.Verify(d => d.DeleteProjectFilesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PullImages_PullsOnTheProjectsHost_WithoutDeploying_AndReturnsOutput()
    {
        var db = TestDb.Create();
        var node = Guid.NewGuid();
        var project = Project("app", node);
        db.ProjectInfo.Add(project);
        db.SaveChanges();
        var (docker, resolver) = Docker();
        docker.Setup(d => d.ComposePullAsync(It.IsAny<DockiUp.Application.Dtos.ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((DockiUp.Application.Dtos.ComposeTarget _, Func<string, Task> log, CancellationToken _) => log(" web Pulled"));

        var output = await new PullProjectImagesCommandHandler(resolver.Object, db, new Mock<IActivityLogger>().Object)
            .Handle(new PullProjectImagesCommand(project.Id), CancellationToken.None);

        resolver.Verify(r => r.Resolve(node));
        Assert.Contains("web Pulled", output);
        docker.Verify(d => d.ComposeUpAsync(It.IsAny<DockiUp.Application.Dtos.ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveContainer_RoutesToNode()
    {
        var node = Guid.NewGuid();
        var (docker, resolver) = Docker();

        await new RemoveContainerCommandHandler(resolver.Object, new Mock<IActivityLogger>().Object)
            .Handle(new RemoveContainerCommand("c1", node), CancellationToken.None);

        resolver.Verify(r => r.Resolve(node));
        docker.Verify(d => d.RemoveContainerAsync("c1"));
    }

    [Fact]
    public async Task DeleteProjectFiles_RefusesPathsOutsideTheProjectsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "dockiup-root-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "dockiup-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var svc = new DockerService(new Mock<IDockiUpDockerClient>().Object, TestDb.Create(),
                Options.Create(new SystemPaths { ProjectsPath = root }), new Mock<IDockiUpProjectConfigurationService>().Object);

            await Assert.ThrowsAsync<ArgumentException>(() => svc.DeleteProjectFilesAsync(outside));
            await Assert.ThrowsAsync<ArgumentException>(() => svc.DeleteProjectFilesAsync(Path.Combine(root, "..", Path.GetFileName(outside))));
            Assert.True(Directory.Exists(outside));

            var inside = Path.Combine(root, "app");
            Directory.CreateDirectory(Path.Combine(inside, ".git"));
            var readOnly = Path.Combine(inside, ".git", "pack");
            File.WriteAllText(readOnly, "x");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            await svc.DeleteProjectFilesAsync(inside);
            Assert.False(Directory.Exists(inside));
        }
        finally
        {
            Directory.Delete(outside, true);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DeploymentTasks_ListsOpenAndRecentWithProjectNames_NewestFirst()
    {
        var db = TestDb.Create();
        var a = Project("alpha");
        var b = Project("beta");
        db.ProjectInfo.AddRange(a, b);
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        db.Deployments.AddRange(
            new Deployment { ProjectId = a.Id, Trigger = DeploymentTrigger.Manual, Status = DeploymentStatus.Succeeded, CreatedAt = t0 },
            new Deployment { ProjectId = a.Id, Trigger = DeploymentTrigger.Manual, Status = DeploymentStatus.Failed, CreatedAt = t0.AddMinutes(1) },
            new Deployment { ProjectId = b.Id, Trigger = DeploymentTrigger.Webhook, Status = DeploymentStatus.Running, CreatedAt = t0.AddMinutes(2) },
            new Deployment { ProjectId = b.Id, Trigger = DeploymentTrigger.Webhook, Status = DeploymentStatus.Queued, CreatedAt = t0.AddMinutes(3) },
            new Deployment { ProjectId = Guid.NewGuid(), Trigger = DeploymentTrigger.Manual, Status = DeploymentStatus.Running, CreatedAt = t0.AddMinutes(4) }); // project gone
        db.SaveChanges();

        var tasks = await new ListDeploymentTasksQueryHandler(db).Handle(new ListDeploymentTasksQuery(RecentLimit: 1), CancellationToken.None);

        Assert.Equal(new[] { DeploymentStatus.Queued, DeploymentStatus.Running, DeploymentStatus.Failed }, tasks.Select(t => t.Deployment.Status));
        Assert.Equal(new[] { "beta", "beta", "alpha" }, tasks.Select(t => t.ProjectName));
    }
}
