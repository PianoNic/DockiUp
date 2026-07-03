using DockiUp.Application.Commands;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Moq;

namespace DockiUp.Tests.Application;

/// <summary>Covers the not-found / no-identifier guard branches of the project lifecycle handlers.</summary>
public class HandlerBranchTests
{
    private static (Mock<IDockerServiceResolver> resolver, DockiUp.Infrastructure.DockiUpDbContext db) Setup()
    {
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<Guid?>())).Returns(new Mock<IDockerService>().Object);
        return (resolver, TestDb.Create());
    }

    [Fact]
    public async Task Restart_UnknownDockerName_Throws()
    {
        var (resolver, db) = Setup();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new RestartProjectCommandHandler(resolver.Object, db).Handle(new RestartProjectCommand(null, "ghost"), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Restart_NoIdentifier_Throws()
    {
        var (resolver, db) = Setup();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RestartProjectCommandHandler(resolver.Object, db).Handle(new RestartProjectCommand(null, null), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Stop_UnknownDockerName_Throws()
    {
        var (resolver, db) = Setup();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new StopProjectCommandHandler(resolver.Object, db).Handle(new StopProjectCommand(null, "ghost"), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Restart_UnknownProjectId_Throws()
    {
        var (resolver, db) = Setup();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new RestartProjectCommandHandler(resolver.Object, db).Handle(new RestartProjectCommand(Guid.NewGuid()), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Update_UnknownProject_Throws()
    {
        var (resolver, db) = Setup();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new UpdateProjectCommandHandler(resolver.Object, db, new Mock<IDockiUpProjectConfigurationService>().Object, new Mock<INodeRpc>().Object)
                .Handle(new UpdateProjectCommand(Guid.NewGuid()), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Update_ComposeProject_RestartsWithoutPulling()
    {
        var db = TestDb.Create();
        var p = new ProjectInfo
        {
            ProjectName = "c", DockerProjectName = "c", ProjectOrigin = ProjectOriginType.Compose,
            ProjectPath = "/c", ComposePath = "/c/y", ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };
        db.ProjectInfo.Add(p); db.SaveChanges();

        var docker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(null)).Returns(docker.Object);
        var config = new Mock<IDockiUpProjectConfigurationService>();
        var rpc = new Mock<INodeRpc>();

        await new UpdateProjectCommandHandler(resolver.Object, db, config.Object, rpc.Object)
            .Handle(new UpdateProjectCommand(p.Id), CancellationToken.None);

        config.Verify(c => c.UpdateRepositoryAsync(It.IsAny<string>()), Times.Never); // not a git project
        rpc.Verify(r => r.InvokeAsync<bool>(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
        docker.Verify(d => d.RestartProjectAsync("/c"), Times.Once);
    }
}
