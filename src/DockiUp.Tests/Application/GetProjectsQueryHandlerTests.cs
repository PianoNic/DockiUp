using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Queries;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Moq;

namespace DockiUp.Tests.Application;

public class GetProjectsQueryHandlerTests
{
    private static ProjectDto Dto(string dockerName, Guid? nodeId = null, bool managed = false) => new()
    {
        Id = null,
        ProjectName = dockerName,
        DockerProjectName = dockerName,
        ProjectDescription = "Not Managed By DockiUp",
        ManagedByDockiUp = managed,
        NodeId = nodeId,
        Containers = [],
    };

    [Fact]
    public async Task Aggregates_Local_And_Node_TaggingAndDedup()
    {
        var db = TestDb.Create();
        var node = Guid.NewGuid();
        // DB rows: one local project, one node-owned project.
        db.ProjectInfo.Add(new ProjectInfo { ProjectName = "Local", DockerProjectName = "localapp", ProjectOrigin = ProjectOriginType.Compose, ProjectPath = "/l", ComposePath = "/l/c", ProjectUpdateMethod = ProjectUpdateMethod.Manual });
        db.ProjectInfo.Add(new ProjectInfo { ProjectName = "Remote", DockerProjectName = "nodeapp", ProjectOrigin = ProjectOriginType.Compose, ProjectPath = "/n", ComposePath = "/n/c", ProjectUpdateMethod = ProjectUpdateMethod.Manual, NodeId = node });
        db.SaveChanges();

        var local = new Mock<IDockerService>();
        // Local daemon reports both (shared-daemon artifact); the node-owned one must be filtered out here.
        local.Setup(d => d.GetProjectsAsync()).ReturnsAsync(new[] { Dto("localapp", null, managed: true), Dto("nodeapp", null, managed: true) });

        var nodeDocker = new Mock<IDockerService>();
        nodeDocker.Setup(d => d.GetRawProjectsAsync()).ReturnsAsync(new[] { Dto("nodeapp") });
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(nodeDocker.Object);

        var directory = new Mock<INodeDirectory>();
        directory.Setup(d => d.GetOnlineNodeIds()).Returns(new[] { node });

        var result = await new GetProjectsQueryHandler(local.Object, resolver.Object, directory.Object, db)
            .Handle(new GetProjectsQuery(), CancellationToken.None);

        Assert.Equal(2, result.Length);
        var localApp = result.Single(p => p.DockerProjectName == "localapp");
        var nodeApp = result.Single(p => p.DockerProjectName == "nodeapp");
        Assert.Null(localApp.NodeId);
        Assert.Equal(node, nodeApp.NodeId);
        Assert.True(nodeApp.ManagedByDockiUp);
        Assert.Equal("Remote", nodeApp.ProjectName);
    }

    [Fact]
    public async Task ManagedProjectWithoutContainers_StillListed_SoItsDeploymentLogIsReachable()
    {
        var db = TestDb.Create();
        var project = new ProjectInfo { ProjectName = "Fresh", DockerProjectName = "fresh", ProjectOrigin = ProjectOriginType.Git, ProjectPath = "/f", ComposePath = "/f/c", ProjectUpdateMethod = ProjectUpdateMethod.Webhook };
        db.ProjectInfo.Add(project);
        db.SaveChanges();

        var local = new Mock<IDockerService>();
        local.Setup(d => d.GetProjectsAsync()).ReturnsAsync(new[] { Dto("other") }); // "fresh" has no containers yet
        var directory = new Mock<INodeDirectory>();
        directory.Setup(d => d.GetOnlineNodeIds()).Returns(Array.Empty<Guid>());

        var result = await new GetProjectsQueryHandler(local.Object, new Mock<IDockerServiceResolver>().Object, directory.Object, db)
            .Handle(new GetProjectsQuery(), CancellationToken.None);

        var fresh = Assert.Single(result, p => p.DockerProjectName == "fresh");
        Assert.Equal(project.Id, fresh.Id);
        Assert.True(fresh.ManagedByDockiUp);
        Assert.Empty(fresh.Containers);
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public async Task ManagedProject_ReportsLatestSuccessfulDeployment_AsDeployedVersion()
    {
        var db = TestDb.Create();
        var project = new ProjectInfo { ProjectName = "App", DockerProjectName = "app", ProjectOrigin = ProjectOriginType.Git, ProjectPath = "/a", ComposePath = "/a/c", ProjectUpdateMethod = ProjectUpdateMethod.Webhook };
        db.ProjectInfo.Add(project);
        var t = DateTime.UtcNow;
        db.Deployments.AddRange(
            new Deployment { ProjectId = project.Id, Trigger = DeploymentTrigger.Manual, Status = DeploymentStatus.Succeeded, CommitAfter = "old", FinishedAt = t.AddMinutes(-5) },
            new Deployment { ProjectId = project.Id, Trigger = DeploymentTrigger.Webhook, Status = DeploymentStatus.Succeeded, CommitAfter = "new", CommitMessage = "fix login", FinishedAt = t },
            new Deployment { ProjectId = project.Id, Trigger = DeploymentTrigger.Manual, Status = DeploymentStatus.Failed, CommitAfter = "broken", FinishedAt = t.AddMinutes(1) });
        db.SaveChanges();

        var local = new Mock<IDockerService>();
        local.Setup(d => d.GetProjectsAsync()).ReturnsAsync(Array.Empty<ProjectDto>());
        var directory = new Mock<INodeDirectory>();
        directory.Setup(d => d.GetOnlineNodeIds()).Returns(Array.Empty<Guid>());

        var app = Assert.Single(await new GetProjectsQueryHandler(local.Object, new Mock<IDockerServiceResolver>().Object, directory.Object, db)
            .Handle(new GetProjectsQuery(), CancellationToken.None));

        Assert.Equal("new", app.DeployedCommit); // a later failure doesn't change what's running
        Assert.Equal("fix login", app.DeployedCommitMessage);
    }

    [Fact]
    public async Task OfflineNode_Throwing_DoesNotFailWholeListing()
    {
        var db = TestDb.Create();
        var node = Guid.NewGuid();
        var local = new Mock<IDockerService>();
        local.Setup(d => d.GetProjectsAsync()).ReturnsAsync(new[] { Dto("localapp", null, managed: true) });
        var nodeDocker = new Mock<IDockerService>();
        nodeDocker.Setup(d => d.GetRawProjectsAsync()).ThrowsAsync(new Exception("node offline"));
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(nodeDocker.Object);
        var directory = new Mock<INodeDirectory>();
        directory.Setup(d => d.GetOnlineNodeIds()).Returns(new[] { node });

        var result = await new GetProjectsQueryHandler(local.Object, resolver.Object, directory.Object, db)
            .Handle(new GetProjectsQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("localapp", result[0].DockerProjectName);
    }
}
