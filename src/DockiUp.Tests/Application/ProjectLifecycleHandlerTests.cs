using DockiUp.Application.Commands;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Moq;

namespace DockiUp.Tests.Application;

/// <summary>Start/stop/restart by compose project name, for DockiUp projects (routed by their row) and
/// for projects DockiUp didn't create (routed by the caller's node id).</summary>
public class ProjectLifecycleHandlerTests
{
    private readonly DockiUp.Infrastructure.DockiUpDbContext _db = TestDb.Create();
    private readonly Mock<IDockerService> _docker = new();
    private readonly Mock<IDockerServiceResolver> _resolver = new();
    private readonly Mock<IActivityLogger> _activity = new();

    public ProjectLifecycleHandlerTests()
    {
        _resolver.Setup(r => r.Resolve(It.IsAny<Guid?>())).Returns(_docker.Object);
    }

    private ProjectLifecycleCommandHandler Handler() => new(_resolver.Object, _db, _activity.Object);

    private ProjectInfo SeedProject(Guid? nodeId = null, string dockerName = "proj")
    {
        var p = new ProjectInfo
        {
            ProjectName = "Proj",
            DockerProjectName = dockerName,
            ProjectOrigin = ProjectOriginType.Compose,
            ProjectPath = "/p",
            ComposePath = "/p/dockiup_compose.yml",
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
            NodeId = nodeId,
        };
        _db.ProjectInfo.Add(p);
        _db.SaveChanges();
        return p;
    }

    [Fact]
    public async Task ById_UsesProjectsNodeAndDockerName()
    {
        var node = Guid.NewGuid();
        var p = SeedProject(node, "web");

        await Handler().Handle(new ProjectLifecycleCommand(ProjectAction.Stop, p.Id, null, NodeId: Guid.NewGuid()), CancellationToken.None);

        _resolver.Verify(r => r.Resolve(node), Times.Once); // the row's node wins over the request's
        _docker.Verify(d => d.StopProjectAsync("web"), Times.Once);
        _activity.Verify(a => a.LogAsync("stop", "web", p.Id, null, It.IsAny<CancellationToken>(), null), Times.Once);
    }

    [Fact]
    public async Task ByName_ManagedProject_UsesItsOwnNode()
    {
        var node = Guid.NewGuid();
        SeedProject(node, "web");

        await Handler().Handle(new ProjectLifecycleCommand(ProjectAction.Restart, null, "web", NodeId: null), CancellationToken.None);

        _resolver.Verify(r => r.Resolve(node), Times.Once);
        _docker.Verify(d => d.RestartProjectAsync("web"), Times.Once);
    }

    [Fact]
    public async Task ByName_UnmanagedProject_UsesRequestNode_NoAdoptionNeeded()
    {
        var node = Guid.NewGuid();

        await Handler().Handle(new ProjectLifecycleCommand(ProjectAction.Start, null, "someone-elses-stack", node), CancellationToken.None);

        _resolver.Verify(r => r.Resolve(node), Times.Once);
        _docker.Verify(d => d.StartProjectAsync("someone-elses-stack"), Times.Once);
        _activity.Verify(a => a.LogAsync("start", "someone-elses-stack", null, null, It.IsAny<CancellationToken>(), null), Times.Once);
    }

    [Fact]
    public async Task UnknownProjectId_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Handler().Handle(new ProjectLifecycleCommand(ProjectAction.Stop, Guid.NewGuid(), null), CancellationToken.None).AsTask());
        _docker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoIdentifier_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Handler().Handle(new ProjectLifecycleCommand(ProjectAction.Stop, null, " "), CancellationToken.None).AsTask());
        _docker.VerifyNoOtherCalls();
    }
}
