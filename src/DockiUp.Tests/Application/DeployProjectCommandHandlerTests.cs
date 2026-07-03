using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Application;

public class DeployProjectCommandHandlerTests : IDisposable
{
    private readonly string _projectsRoot = Path.Combine(Path.GetTempPath(), "dockiup-tests-" + Guid.NewGuid().ToString("N"));

    private (DeployProjectCommandHandler handler, Mock<IDockerService> docker, Mock<IDockiUpProjectConfigurationService> config,
        Mock<IActivityLogger> activity, Mock<INodeRpc> rpc, DockiUp.Infrastructure.DockiUpDbContext db) Build()
    {
        var docker = new Mock<IDockerService>();
        var config = new Mock<IDockiUpProjectConfigurationService>();
        config.Setup(c => c.WriteComposeFileAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string path, string _) => Path.Combine(path, "dockiup_compose.yml"));
        var activity = new Mock<IActivityLogger>();
        var rpc = new Mock<INodeRpc>();
        var db = TestDb.Create();
        var paths = Options.Create(new SystemPaths { ProjectsPath = _projectsRoot });
        var handler = new DeployProjectCommandHandler(docker.Object, paths, config.Object, db, activity.Object, rpc.Object);
        return (handler, docker, config, activity, rpc, db);
    }

    [Fact]
    public async Task Compose_DeploysLocally_PersistsProject_AndLogs()
    {
        var (handler, docker, config, activity, rpc, db) = Build();
        var dto = new SetupProjectDto
        {
            ProjectName = "My App",
            ProjectOrigin = ProjectOriginType.Compose,
            Compose = "services: {}",
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };

        await handler.Handle(new DeployProjectCommand(dto), CancellationToken.None);

        var saved = await db.ProjectInfo.SingleAsync();
        Assert.Equal("My App", saved.ProjectName);
        Assert.Equal("myapp", saved.DockerProjectName); // lowercased, whitespace stripped
        Assert.Null(saved.NodeId);
        Assert.Equal(ProjectOriginType.Compose, saved.ProjectOrigin);
        config.Verify(c => c.WriteComposeFileAsync(It.IsAny<string>(), "services: {}"), Times.Once);
        docker.Verify(d => d.StartProjectAsync(It.IsAny<string>()), Times.Once);
        rpc.Verify(r => r.InvokeAsync<NodeDeployResultDto>(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
        activity.Verify(a => a.LogAsync("deploy", "My App", null, "Compose", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Git_DeploysLocally_ClonesThenComposes()
    {
        var (handler, docker, config, _, _, db) = Build();
        var dto = new SetupProjectDto
        {
            ProjectName = "git-app",
            ProjectOrigin = ProjectOriginType.Git,
            GitUrl = "file:///repo.git",
            Compose = "services: {}",
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };

        await handler.Handle(new DeployProjectCommand(dto), CancellationToken.None);

        config.Verify(c => c.CloneRepositoryAsync(It.IsAny<string>(), "file:///repo.git"), Times.Once);
        docker.Verify(d => d.StartProjectAsync(It.IsAny<string>()), Times.Once);
        Assert.Equal("file:///repo.git", (await db.ProjectInfo.SingleAsync()).GitUrl);
    }

    [Fact]
    public async Task WithNodeId_ShipsToNode_PersistsReturnedPaths_NoLocalStart()
    {
        var (handler, docker, config, _, rpc, db) = Build();
        var node = Guid.NewGuid();
        rpc.Setup(r => r.InvokeAsync<NodeDeployResultDto>(node, "DeployProject", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeDeployResultDto("/node/path", "/node/path/dockiup_compose.yml"));
        var dto = new SetupProjectDto
        {
            ProjectName = "remote",
            ProjectOrigin = ProjectOriginType.Compose,
            Compose = "services: {}",
            NodeId = node,
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };

        await handler.Handle(new DeployProjectCommand(dto), CancellationToken.None);

        var saved = await db.ProjectInfo.SingleAsync();
        Assert.Equal(node, saved.NodeId);
        Assert.Equal("/node/path", saved.ProjectPath);
        Assert.Equal("/node/path/dockiup_compose.yml", saved.ComposePath);
        rpc.Verify(r => r.InvokeAsync<NodeDeployResultDto>(node, "DeployProject", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        docker.Verify(d => d.StartProjectAsync(It.IsAny<string>()), Times.Never); // node runs it, not us
        config.Verify(c => c.WriteComposeFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Import_LogsOnly_DoesNotPersist()
    {
        var (handler, docker, _, activity, _, db) = Build();
        var dto = new SetupProjectDto
        {
            ProjectName = "imported",
            ProjectOrigin = ProjectOriginType.Import,
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };

        await handler.Handle(new DeployProjectCommand(dto), CancellationToken.None);

        Assert.Empty(db.ProjectInfo);
        docker.Verify(d => d.StartProjectAsync(It.IsAny<string>()), Times.Never);
        activity.Verify(a => a.LogAsync("deploy", "imported", null, "Import", It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_projectsRoot)) Directory.Delete(_projectsRoot, recursive: true); } catch { }
    }
}
