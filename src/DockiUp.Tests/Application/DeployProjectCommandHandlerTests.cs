using DockiUp.Application.Commands;
using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Application;

/// <summary>Creating a project: files are prepared where it will run, the row is stored, and the first
/// deployment is queued (the queue - not this handler - runs compose).</summary>
public class DeployProjectCommandHandlerTests : IDisposable
{
    private readonly string _projectsRoot = Path.Combine(Path.GetTempPath(), "dockiup-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IDockiUpProjectConfigurationService> _files = new();
    private readonly Mock<IActivityLogger> _activity = new();
    private readonly Mock<INodeRpc> _rpc = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly DockiUp.Infrastructure.DockiUpDbContext _db = TestDb.Create();

    public DeployProjectCommandHandlerTests()
    {
        _files.Setup(c => c.WriteComposeFileAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string path, string _) => Path.Combine(path, "dockiup_compose.yml"));
        _mediator.Setup(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns((QueueDeploymentCommand c, CancellationToken _) => new ValueTask<DeploymentDto>(new DeploymentDto(
                Guid.NewGuid(), c.ProjectId, c.Trigger, DeploymentStatus.Queued, null, null, null, null, null, DateTime.UtcNow, null, null, null)));
    }

    private DeployProjectCommandHandler Handler() => new(
        Options.Create(new SystemPaths { ProjectsPath = _projectsRoot }), _files.Object, _db, _activity.Object, _rpc.Object, _mediator.Object);

    private static SetupProjectDto Compose(string name = "My App", Guid? node = null) => new()
    {
        ProjectName = name,
        ProjectOrigin = ProjectOriginType.Compose,
        Compose = "services: {}",
        NodeId = node,
        ProjectUpdateMethod = ProjectUpdateMethod.Manual,
    };

    [Fact]
    public async Task Compose_PreparesLocally_PersistsProject_AndQueuesCreateDeployment()
    {
        var queued = await Handler().Handle(new DeployProjectCommand(Compose()), CancellationToken.None);

        var saved = await _db.ProjectInfo.SingleAsync();
        Assert.Equal("My App", saved.ProjectName);
        Assert.Equal("myapp", saved.DockerProjectName); // lowercased, whitespace stripped
        Assert.Null(saved.NodeId);
        Assert.Null(saved.Branch);
        Assert.Equal(48, saved.WebhookSecret.Length); // 24 random bytes, hex
        Assert.Equal(Path.Combine(_projectsRoot, "My App", "dockiup_compose.yml"), saved.ComposePath);
        _files.Verify(c => c.WriteComposeFileAsync(It.IsAny<string>(), "services: {}"), Times.Once);

        Assert.NotNull(queued);
        Assert.Equal(DeploymentTrigger.Create, queued!.Trigger);
        _mediator.Verify(m => m.Send(It.Is<QueueDeploymentCommand>(c => c.ProjectId == saved.Id && c.Trigger == DeploymentTrigger.Create),
            It.IsAny<CancellationToken>()), Times.Once);
        _activity.Verify(a => a.LogAsync("create", "My App", saved.Id, "Compose", It.IsAny<CancellationToken>(), null), Times.Once);
    }

    [Fact]
    public async Task Git_ClonesRequestedBranch_UsesRepoComposeFile_AndRecordsBranch()
    {
        _files.Setup(c => c.CloneRepositoryAsync(It.IsAny<string>(), "file:///repo.git", "release"))
            .Callback((string path, string _, string? _, DockiUp.Application.Git.GitCredentials? _) =>
            {
                Directory.CreateDirectory(Path.Combine(path, "deploy"));
                File.WriteAllText(Path.Combine(path, "deploy", "compose.yml"), "services: {}");
            })
            .ReturnsAsync("release");
        var dto = new SetupProjectDto
        {
            ProjectName = "git-app",
            ProjectOrigin = ProjectOriginType.Git,
            GitUrl = "file:///repo.git",
            Branch = "release",
            ComposeFile = "deploy/compose.yml",
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };

        await Handler().Handle(new DeployProjectCommand(dto), CancellationToken.None);

        var saved = await _db.ProjectInfo.SingleAsync();
        Assert.Equal("release", saved.Branch);
        Assert.Equal(Path.GetFullPath(Path.Combine(_projectsRoot, "git-app", "deploy", "compose.yml")), saved.ComposePath);
        _files.Verify(c => c.WriteComposeFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never); // repo's own compose
    }

    [Fact]
    public async Task WithNodeId_PreparesOnNode_PersistsReturnedPaths_NothingLocal()
    {
        var node = Guid.NewGuid();
        _rpc.Setup(r => r.InvokeAsync<PreparedProject>(node, "PrepareProject", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreparedProject("/node/path", "/node/path/dockiup_compose.yml", null));

        await Handler().Handle(new DeployProjectCommand(Compose("remote", node)), CancellationToken.None);

        var saved = await _db.ProjectInfo.SingleAsync();
        Assert.Equal(node, saved.NodeId);
        Assert.Equal("/node/path", saved.ProjectPath);
        Assert.Equal("/node/path/dockiup_compose.yml", saved.ComposePath);
        _files.Verify(c => c.WriteComposeFileAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.False(Directory.Exists(Path.Combine(_projectsRoot, "remote")));
    }

    [Fact]
    public async Task Import_LogsOnly_DoesNotPersistOrQueue()
    {
        var dto = new SetupProjectDto { ProjectName = "imported", ProjectOrigin = ProjectOriginType.Import, ProjectUpdateMethod = ProjectUpdateMethod.Manual };

        var result = await Handler().Handle(new DeployProjectCommand(dto), CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(_db.ProjectInfo);
        _mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        _activity.Verify(a => a.LogAsync("deploy", "imported", null, "Import", It.IsAny<CancellationToken>(), null), Times.Once);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_projectsRoot)) Directory.Delete(_projectsRoot, recursive: true); } catch { }
    }
}
