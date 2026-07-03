using DockiUp.Application.Commands;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Moq;

namespace DockiUp.Tests.Application;

public class ProjectLifecycleHandlerTests
{
    private static ProjectInfo SeedProject(DockiUp.Infrastructure.DockiUpDbContext db, Guid? nodeId = null, string path = "/p", string dockerName = "proj")
    {
        var p = new ProjectInfo
        {
            ProjectName = "Proj",
            DockerProjectName = dockerName,
            ProjectOrigin = ProjectOriginType.Compose,
            ProjectPath = path,
            ComposePath = path + "/dockiup_compose.yml",
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
            NodeId = nodeId,
        };
        db.ProjectInfo.Add(p);
        db.SaveChanges();
        return p;
    }

    [Fact]
    public async Task Stop_ByProjectId_RoutesToOwningNode()
    {
        var db = TestDb.Create();
        var node = Guid.NewGuid();
        var p = SeedProject(db, nodeId: node, path: "/n/proj");
        var docker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(docker.Object);

        await new StopProjectCommandHandler(resolver.Object, db)
            .Handle(new StopProjectCommand(p.Id), CancellationToken.None);

        resolver.Verify(r => r.Resolve(node), Times.Once);
        docker.Verify(d => d.StopProjectAsync("/n/proj"), Times.Once);
    }

    [Fact]
    public async Task Restart_ByDockerName_RoutesLocally_WhenNoNode()
    {
        var db = TestDb.Create();
        SeedProject(db, nodeId: null, path: "/local/proj", dockerName: "web");
        var docker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(null)).Returns(docker.Object);

        await new RestartProjectCommandHandler(resolver.Object, db)
            .Handle(new RestartProjectCommand(null, "web"), CancellationToken.None);

        docker.Verify(d => d.RestartProjectAsync("/local/proj"), Times.Once);
    }

    [Fact]
    public async Task Stop_UnknownProject_Throws()
    {
        var db = TestDb.Create();
        var resolver = new Mock<IDockerServiceResolver>();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new StopProjectCommandHandler(resolver.Object, db)
                .Handle(new StopProjectCommand(Guid.NewGuid()), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Stop_NoIdentifier_Throws()
    {
        var db = TestDb.Create();
        var resolver = new Mock<IDockerServiceResolver>();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new StopProjectCommandHandler(resolver.Object, db)
                .Handle(new StopProjectCommand(null, null), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Update_GitOnNode_PullsViaRpc_ThenRestartsViaResolver()
    {
        var db = TestDb.Create();
        var node = Guid.NewGuid();
        var p = new ProjectInfo
        {
            ProjectName = "g", DockerProjectName = "g", ProjectOrigin = ProjectOriginType.Git,
            ProjectPath = "/n/g", ComposePath = "/n/g/c.yml", ProjectUpdateMethod = ProjectUpdateMethod.Manual, NodeId = node,
        };
        db.ProjectInfo.Add(p); db.SaveChanges();

        var docker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(docker.Object);
        var config = new Mock<IDockiUpProjectConfigurationService>();
        var rpc = new Mock<INodeRpc>();
        rpc.Setup(r => r.InvokeAsync<bool>(node, "PullRepository", It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await new UpdateProjectCommandHandler(resolver.Object, db, config.Object, rpc.Object)
            .Handle(new UpdateProjectCommand(p.Id), CancellationToken.None);

        rpc.Verify(r => r.InvokeAsync<bool>(node, "PullRepository", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        config.Verify(c => c.UpdateRepositoryAsync(It.IsAny<string>()), Times.Never); // node pulls, not us
        docker.Verify(d => d.RestartProjectAsync("/n/g"), Times.Once);
    }

    [Fact]
    public async Task Update_GitLocal_PullsLocally_ThenRestarts()
    {
        var db = TestDb.Create();
        var p = new ProjectInfo
        {
            ProjectName = "g", DockerProjectName = "g", ProjectOrigin = ProjectOriginType.Git,
            ProjectPath = "/g", ComposePath = "/g/c.yml", ProjectUpdateMethod = ProjectUpdateMethod.Manual, NodeId = null,
        };
        db.ProjectInfo.Add(p); db.SaveChanges();

        var docker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(null)).Returns(docker.Object);
        var config = new Mock<IDockiUpProjectConfigurationService>();
        var rpc = new Mock<INodeRpc>();

        await new UpdateProjectCommandHandler(resolver.Object, db, config.Object, rpc.Object)
            .Handle(new UpdateProjectCommand(p.Id), CancellationToken.None);

        config.Verify(c => c.UpdateRepositoryAsync("/g"), Times.Once);
        docker.Verify(d => d.RestartProjectAsync("/g"), Times.Once);
    }
}
