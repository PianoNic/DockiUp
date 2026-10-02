using DockiUp.Application;
using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DockiUp.Tests.Application;

/// <summary>The deploy pipeline: runner outcomes per trigger, the queue command, and project preparation.</summary>
public class DeploymentRunnerTests
{
    private readonly DockiUp.Infrastructure.DockiUpDbContext _db = TestDb.Create();
    private readonly Mock<IDockerService> _docker = new();
    private readonly Mock<IDockerServiceResolver> _resolver = new();
    private readonly Mock<IDeploymentEvents> _events = new();
    private readonly Mock<IActivityLogger> _activity = new();
    private readonly List<DeploymentDto> _changes = [];

    public DeploymentRunnerTests()
    {
        _resolver.Setup(r => r.Resolve(It.IsAny<Guid?>())).Returns(_docker.Object);
        _events.Setup(e => e.ChangedAsync(It.IsAny<DeploymentDto>())).Callback<DeploymentDto>(_changes.Add).Returns(Task.CompletedTask);
        _events.Setup(e => e.LogAsync(It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);
    }

    private DeploymentRunner Runner() => new(_db, _resolver.Object, _events.Object, _activity.Object);

    private ProjectInfo Seed(ProjectOriginType origin, Guid? node = null, string? branch = "main")
    {
        var p = new ProjectInfo
        {
            ProjectName = "App", DockerProjectName = "app", ProjectOrigin = origin, NodeId = node, Branch = branch,
            ProjectPath = "/p/app", ComposePath = "/p/app/docker-compose.yml", ProjectUpdateMethod = ProjectUpdateMethod.Periodically,
            PeriodicIntervalInMinutes = 5,
        };
        _db.ProjectInfo.Add(p);
        _db.SaveChanges();
        return p;
    }

    private Deployment Queued(ProjectInfo p, DeploymentTrigger trigger, string? actor = null)
    {
        var d = new Deployment { ProjectId = p.Id, Trigger = trigger, ActorName = actor };
        _db.Deployments.Add(d);
        _db.SaveChanges();
        return d;
    }

    private void Git(string before, string after) => _docker
        .Setup(d => d.SyncRepositoryAsync("/p/app", "main", It.IsAny<string?>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
        .Returns(async (string _, string? _, string? _, Func<string, Task> log, CancellationToken _, DockiUp.Application.Git.GitCredentials? _) =>
        {
            await log($"git {before}->{after}");
            return new GitSyncResult(before, after, "main");
        });

    private void Compose(bool changed) => _docker
        .Setup(d => d.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
        .Returns(async (ComposeTarget _, Func<string, Task> log, CancellationToken _) =>
        {
            await log("compose up");
            return new ComposeUpResult(changed);
        });

    [Fact]
    public async Task Manual_GitDeploy_Succeeds_RecordsCommits_StreamsLog_AndReportsRunningThenSucceeded()
    {
        var node = Guid.NewGuid();
        var p = Seed(ProjectOriginType.Git, node);
        var d = Queued(p, DeploymentTrigger.Manual, "ada");
        Git("aaa1111", "bbb2222");
        Compose(changed: true);

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id, "ada"), CancellationToken.None);

        var saved = await _db.Deployments.SingleAsync();
        Assert.Equal(DeploymentStatus.Succeeded, saved.Status);
        Assert.Equal("aaa1111", saved.CommitBefore);
        Assert.Equal("bbb2222", saved.CommitAfter);
        Assert.NotNull(saved.StartedAt);
        Assert.NotNull(saved.FinishedAt);
        Assert.Contains("git aaa1111->bbb2222", saved.Log);
        Assert.Contains("compose up", saved.Log);
        Assert.Equal([DeploymentStatus.Running, DeploymentStatus.Succeeded], _changes.Select(c => c.Status));
        _events.Verify(e => e.LogAsync(d.Id, "compose up"), Times.Once);
        _resolver.Verify(r => r.Resolve(node), Times.AtLeastOnce); // runs on the project's node
        _docker.Verify(x => x.ComposeUpAsync(It.Is<ComposeTarget>(t =>
            t.ProjectPath == "/p/app" && t.ComposePath == "/p/app/docker-compose.yml" && t.DockerProjectName == "app"),
            It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
        _activity.Verify(a => a.LogAsync("deploy", "App", p.Id, "Manual @ bbb2222", It.IsAny<CancellationToken>(), "ada"), Times.Once);
    }

    [Fact]
    public async Task Manual_GitDeploy_WithoutNewCommits_StillRedeploys()
    {
        var p = Seed(ProjectOriginType.Git);
        var d = Queued(p, DeploymentTrigger.Manual);
        Git("same", "same");
        Compose(changed: false);

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id), CancellationToken.None);

        Assert.Equal(DeploymentStatus.Succeeded, (await _db.Deployments.SingleAsync()).Status);
        _docker.Verify(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Manual_Deploy_ClearsPendingImageUpdates()
    {
        var p = Seed(ProjectOriginType.Compose);
        _db.ImageUpdates.Add(new ImageUpdateStatus { ProjectId = p.Id, ServiceName = "web", Image = "nginx", UpdateAvailable = true, CurrentDigest = "sha256:old", LatestDigest = "sha256:new" });
        _db.SaveChanges();
        var d = Queued(p, DeploymentTrigger.Manual);
        Compose(changed: false);

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id), CancellationToken.None);

        var row = await _db.ImageUpdates.SingleAsync();
        Assert.False(row.UpdateAvailable);
        Assert.Equal("sha256:new", row.CurrentDigest);
    }

    [Fact]
    public async Task Periodic_Git_NoNewCommits_LeavesNoRecord_AndDoesNotTouchContainers()
    {
        var p = Seed(ProjectOriginType.Git);
        Git("same", "same");

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Periodic), CancellationToken.None);

        Assert.Empty(_db.Deployments);
        Assert.NotNull((await _db.ProjectInfo.SingleAsync()).LastPeriodicUpdateAt);
        _docker.Verify(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        _events.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Periodic_Git_NewCommit_DeploysAndRecords()
    {
        var p = Seed(ProjectOriginType.Git);
        Git("old", "new");
        Compose(changed: true);

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Periodic), CancellationToken.None);

        var saved = await _db.Deployments.SingleAsync();
        Assert.Equal(DeploymentTrigger.Periodic, saved.Trigger);
        Assert.Equal(DeploymentStatus.Succeeded, saved.Status);
        Assert.Contains("git old->new", saved.Log); // buffered before the record existed
        Assert.NotNull((await _db.ProjectInfo.SingleAsync()).LastPeriodicUpdateAt);
    }

    [Fact]
    public async Task Periodic_Compose_NothingRecreated_LeavesNoRecord()
    {
        var p = Seed(ProjectOriginType.Compose, branch: null);
        Compose(changed: false);

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Periodic), CancellationToken.None);

        Assert.Empty(_db.Deployments);
        Assert.NotNull((await _db.ProjectInfo.SingleAsync()).LastPeriodicUpdateAt);
        _docker.Verify(x => x.SyncRepositoryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Periodic_Compose_NewerImagePulled_Records()
    {
        var p = Seed(ProjectOriginType.Compose, branch: null);
        Compose(changed: true);

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Periodic), CancellationToken.None);

        Assert.Equal(DeploymentStatus.Succeeded, (await _db.Deployments.SingleAsync()).Status);
    }

    [Fact]
    public async Task ComposeFailure_MarksFailed_WithMessage_AndErrorInLog()
    {
        var p = Seed(ProjectOriginType.Git);
        var d = Queued(p, DeploymentTrigger.Webhook, "GitHub webhook");
        Git("a", "b");
        _docker.Setup(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("docker compose up failed (exit 1): port is already allocated"));

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Webhook, d.Id, "GitHub webhook"), CancellationToken.None);

        var saved = await _db.Deployments.SingleAsync();
        Assert.Equal(DeploymentStatus.Failed, saved.Status);
        Assert.Contains("port is already allocated", saved.Error);
        Assert.Contains("ERROR:", saved.Log);
        Assert.Equal("b", saved.CommitAfter); // the sync still happened
        Assert.Equal(DeploymentStatus.Failed, _changes.Last().Status);
        _activity.Verify(a => a.LogAsync("deploy.failed", "App", p.Id, It.IsAny<string>(), It.IsAny<CancellationToken>(), "GitHub webhook"), Times.Once);
    }

    [Fact]
    public async Task Periodic_Failure_IsRecordedEvenThoughQuietRunsAreNot()
    {
        var p = Seed(ProjectOriginType.Compose, branch: null);
        _docker.Setup(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("registry unreachable"));

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Periodic), CancellationToken.None);

        var saved = await _db.Deployments.SingleAsync();
        Assert.Equal(DeploymentStatus.Failed, saved.Status);
        Assert.Equal("registry unreachable", saved.Error);
    }

    [Fact]
    public async Task ProjectDeleted_QueuedDeploymentFails_Cleanly()
    {
        var p = Seed(ProjectOriginType.Compose);
        var d = Queued(p, DeploymentTrigger.Manual);
        _db.ProjectInfo.Remove(p);
        _db.SaveChanges();

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id), CancellationToken.None);

        var saved = await _db.Deployments.SingleAsync();
        Assert.Equal(DeploymentStatus.Failed, saved.Status);
        _docker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnknownBranch_RecordsBranchFromFirstSync()
    {
        var p = Seed(ProjectOriginType.Git, branch: null);
        var d = Queued(p, DeploymentTrigger.Manual);
        _docker.Setup(x => x.SyncRepositoryAsync("/p/app", null, It.IsAny<string?>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitSyncResult("a", "a", "develop"));
        Compose(changed: false);

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id), CancellationToken.None);

        Assert.Equal("develop", (await _db.ProjectInfo.SingleAsync()).Branch);
    }
}

public class QueueDeploymentCommandTests
{
    [Fact]
    public async Task CreatesQueuedRow_AnnouncesIt_AndEnqueuesWithItsId()
    {
        var db = TestDb.Create();
        var project = new ProjectInfo
        {
            ProjectName = "App", DockerProjectName = "app", ProjectOrigin = ProjectOriginType.Compose,
            ProjectPath = "/p", ComposePath = "/p/c.yml", ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };
        db.ProjectInfo.Add(project);
        db.SaveChanges();
        var queue = new Mock<IDeploymentQueue>();
        var events = new Mock<IDeploymentEvents>();

        var dto = await new QueueDeploymentCommandHandler(db, queue.Object, events.Object)
            .Handle(new QueueDeploymentCommand(project.Id, DeploymentTrigger.Manual, "ada"), CancellationToken.None);

        var row = await db.Deployments.SingleAsync();
        Assert.Equal(row.Id, dto.Id);
        Assert.Equal(DeploymentStatus.Queued, row.Status);
        Assert.Equal("ada", row.ActorName);
        events.Verify(e => e.ChangedAsync(It.Is<DeploymentDto>(x => x.Id == row.Id && x.Status == DeploymentStatus.Queued)), Times.Once);
        queue.Verify(q => q.TryEnqueue(new DeploymentRequest(project.Id, DeploymentTrigger.Manual, row.Id, "ada")), Times.Once);
    }

    [Fact]
    public async Task UnknownProject_Throws_AndQueuesNothing()
    {
        var queue = new Mock<IDeploymentQueue>();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new QueueDeploymentCommandHandler(TestDb.Create(), queue.Object, new Mock<IDeploymentEvents>().Object)
            .Handle(new QueueDeploymentCommand(Guid.NewGuid(), DeploymentTrigger.Manual), CancellationToken.None).AsTask());
        queue.VerifyNoOtherCalls();
    }
}

public class ProjectPreparerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dockiup-prep-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IDockiUpProjectConfigurationService> _files = new();

    private static SetupProjectDto Dto(ProjectOriginType origin, string? composeFile = null) => new()
    {
        ProjectName = "app",
        ProjectOrigin = origin,
        GitUrl = "file:///r.git",
        Compose = "services: {}",
        ComposeFile = composeFile,
        ProjectUpdateMethod = ProjectUpdateMethod.Manual,
    };

    [Fact]
    public async Task ComposeOrigin_WritesUiComposeFile_NoBranch()
    {
        _files.Setup(f => f.WriteComposeFileAsync(It.IsAny<string>(), "services: {}"))
            .ReturnsAsync((string path, string _) => Path.Combine(path, "dockiup_compose.yml"));

        var prepared = await ProjectPreparer.PrepareAsync(Dto(ProjectOriginType.Compose), _root, _files.Object);

        Assert.Equal(Path.Combine(_root, "app"), prepared.ProjectPath);
        Assert.Equal(Path.Combine(_root, "app", "dockiup_compose.yml"), prepared.ComposePath);
        Assert.Null(prepared.Branch);
        Assert.True(Directory.Exists(prepared.ProjectPath));
        _files.Verify(f => f.CloneRepositoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task GitOrigin_DefaultsToRepoDockerCompose_AndReportsClonedBranch()
    {
        _files.Setup(f => f.CloneRepositoryAsync(It.IsAny<string>(), "file:///r.git", null))
            .Callback((string path, string _, string? _, DockiUp.Application.Git.GitCredentials? _) => File.WriteAllText(Path.Combine(path, "docker-compose.yml"), "services: {}"))
            .ReturnsAsync("main");

        var prepared = await ProjectPreparer.PrepareAsync(Dto(ProjectOriginType.Git), _root, _files.Object);

        Assert.Equal(Path.Combine(_root, "app", "docker-compose.yml"), prepared.ComposePath);
        Assert.Equal("main", prepared.Branch);
    }

    [Fact]
    public async Task GitOrigin_ComposeFileMissingInRepo_Throws()
    {
        _files.Setup(f => f.CloneRepositoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync("main");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => ProjectPreparer.PrepareAsync(Dto(ProjectOriginType.Git), _root, _files.Object));
        Assert.Contains("docker-compose.yml", ex.Message);
        // The half-prepared folder is removed, so retrying under the same name works.
        Assert.Empty(Directory.Exists(_root) ? Directory.EnumerateDirectories(_root) : []);
    }

    [Fact]
    public async Task GitOrigin_ComposeFileEscapingRepo_Throws()
    {
        _files.Setup(f => f.CloneRepositoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync("main");
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "outside.yml"), "services: {}");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ProjectPreparer.PrepareAsync(Dto(ProjectOriginType.Git, "../outside.yml"), _root, _files.Object));
    }

    [Fact]
    public async Task ExistingNonEmptyFolder_Throws_WithoutTouchingIt()
    {
        var existing = Path.Combine(_root, "app");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "mine");

        await Assert.ThrowsAsync<ArgumentException>(() => ProjectPreparer.PrepareAsync(Dto(ProjectOriginType.Compose), _root, _files.Object));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(existing, "keep.txt")));
        _files.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }
}
