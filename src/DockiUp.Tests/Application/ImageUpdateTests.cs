using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Application.ImageUpdates;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DockiUp.Tests.Application;

/// <summary>Image references, digest comparison, tag order and the override file: the pure parts of #68-#70.</summary>
public class ImageReferenceAndOverrideTests
{
    [Theory]
    [InlineData("nginx", "docker.io", "library/nginx", "latest", "nginx")]
    [InlineData("nginx:1.27-alpine", "docker.io", "library/nginx", "1.27-alpine", "nginx")]
    [InlineData("docker.io/library/nginx:1", "docker.io", "library/nginx", "1", "nginx")]
    [InlineData("index.docker.io/grafana/grafana", "docker.io", "grafana/grafana", "latest", "grafana/grafana")]
    [InlineData("ghcr.io/owner/app:v2", "ghcr.io", "owner/app", "v2", "ghcr.io/owner/app")]
    [InlineData("localhost:5000/team/app", "localhost:5000", "team/app", "latest", "localhost:5000/team/app")]
    [InlineData("registry.example.com:8443/a/b/c:1.0", "registry.example.com:8443", "a/b/c", "1.0", "registry.example.com:8443/a/b/c")]
    public void Parse_AppliesDockerDefaults(string input, string registry, string repository, string tag, string name)
    {
        Assert.True(ImageReference.TryParse(input, out var r));
        Assert.Equal(registry, r.Registry);
        Assert.Equal(repository, r.Repository);
        Assert.Equal(tag, r.Tag);
        Assert.Equal(name, r.Name);
        Assert.Null(r.Digest);
    }

    [Fact]
    public void Parse_Digest_AndApiHost()
    {
        Assert.True(ImageReference.TryParse("nginx@sha256:abc", out var r));
        Assert.Equal("sha256:abc", r.Digest);
        Assert.Equal("registry-1.docker.io", r.ApiHost);
        Assert.Equal("nginx@sha256:abc", r.ToString());
        Assert.Equal("nginx:1.27", r.WithTag("1.27").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPPER/case")]
    [InlineData("nginx:bad tag")]
    [InlineData("nginx@nodigest")]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef!")]
    public void Parse_RejectsInvalid(string input) => Assert.False(ImageReference.TryParse(input, out _));

    [Fact]
    public void CurrentDigest_PrefersTheSameRepository_FallsBackToAny_NullWhenBuiltLocally()
    {
        ImageReference.TryParse("nginx:latest", out var nginx);
        Assert.Equal("sha256:b", ImageDigests.CurrentDigest(nginx!, ["mirror.example.com/nginx@sha256:a", "nginx@sha256:b"]));
        Assert.Equal("sha256:a", ImageDigests.CurrentDigest(nginx!, ["mirror.example.com/nginx@sha256:a"]));
        Assert.Null(ImageDigests.CurrentDigest(nginx!, []));
    }

    [Fact]
    public void IsNewer_ComparesAgainstEveryKnownDigest()
    {
        Assert.False(ImageDigests.IsNewer("sha256:b", ["mirror/x@sha256:a", "nginx@sha256:b"]));
        Assert.True(ImageDigests.IsNewer("sha256:c", ["nginx@sha256:b"]));
    }

    [Fact]
    public void NewestFirst_LatestThenVersionsDescendingThenOthers()
    {
        var sorted = ImageDigests.NewestFirst(["alpine", "1.9", "1.27.3-alpine", "latest", "1.27.3", "1.27", "v2.0.0", "1.10.1", "mainline", "1.27"]);
        Assert.Equal(["latest", "v2.0.0", "1.27", "1.27.3", "1.27.3-alpine", "1.10.1", "1.9", "alpine", "mainline"], sorted);
    }

    [Fact]
    public void ComposeArgs_AddOverrideBetweenComposeAndEnvFile_OnlyWhenItExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agenttest-override-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var compose = Path.Combine(dir, "compose.yml");
            File.WriteAllText(compose, "services: {}");
            var target = new ComposeTarget(dir, compose, "app");
            Assert.Equal(["-p", "app", "-f", compose], DockiUp.Infrastructure.Services.DockerService.ComposeProjectArgs(target));

            var overrideFile = Path.Combine(dir, ComposeOverrideFile.FileName);
            File.WriteAllText(overrideFile, "services: {}");
            var env = Path.Combine(dir, ".env");
            File.WriteAllText(env, "A=1");
            // Same order for deploy, pull and validation: compose file(s), pinned tags, then the env file.
            Assert.Equal(["-p", "app", "-f", compose, "-f", overrideFile, "--env-file", env],
                DockiUp.Infrastructure.Services.DockerService.ComposeProjectArgs(target));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetImage_CreatesTheFile_AndReadsBack()
    {
        var yaml = ComposeOverrideFile.SetImage(null, "web", "nginx:1.27");
        Assert.NotNull(yaml);
        Assert.StartsWith("# Managed by DockiUp", yaml);
        Assert.Equal(new Dictionary<string, string> { ["web"] = "nginx:1.27" }, ComposeOverrideFile.ReadImages(yaml));
    }

    [Fact]
    public void SetImage_MergesWithOtherServicesAndKeys_AndRemovesOnlyThePin()
    {
        var existing = """
            services:
              web:
                image: nginx:1.25
                environment:
                  A: "1"
              db:
                image: postgres:16
            x-note: keep me
            """;

        var updated = ComposeOverrideFile.SetImage(existing, "web", "nginx:1.27")!;
        Assert.Equal(new Dictionary<string, string> { ["web"] = "nginx:1.27", ["db"] = "postgres:16" }, ComposeOverrideFile.ReadImages(updated));
        Assert.Contains("A: 1", updated.Replace("\"", "").Replace("'", ""));
        Assert.Contains("x-note: keep me", updated);

        var reset = ComposeOverrideFile.SetImage(updated, "web", null)!;
        Assert.Equal(new Dictionary<string, string> { ["db"] = "postgres:16" }, ComposeOverrideFile.ReadImages(reset));
        Assert.Contains("environment", reset); // the web entry stays for its other keys
    }

    [Fact]
    public void SetImage_RemovingTheLastPin_MeansDeleteTheFile()
    {
        var yaml = ComposeOverrideFile.SetImage(null, "web", "nginx:1.27");
        Assert.Null(ComposeOverrideFile.SetImage(yaml, "web", null));
        Assert.Null(ComposeOverrideFile.SetImage(null, "web", null));
        Assert.Empty(ComposeOverrideFile.ReadImages(null));
    }

    [Fact]
    public void SetImage_InvalidYaml_IsABadRequest()
        => Assert.Throws<ArgumentException>(() => ComposeOverrideFile.SetImage("services: [unclosed", "web", "x"));

    [Theory]
    [InlineData(ImageUpdatePolicy.Off, true, true, false, false, false)]
    [InlineData(ImageUpdatePolicy.Notify, true, true, false, true, false)]
    [InlineData(ImageUpdatePolicy.Notify, true, false, false, false, false)] // already reported
    [InlineData(ImageUpdatePolicy.Auto, true, true, false, true, true)]
    [InlineData(ImageUpdatePolicy.Auto, true, false, false, false, true)]   // still outstanding (e.g. last run failed): retry
    [InlineData(ImageUpdatePolicy.Auto, true, false, true, false, false)]   // one already queued/running
    [InlineData(ImageUpdatePolicy.Auto, false, false, false, false, false)]
    public void Decide_AppliesThePolicy(ImageUpdatePolicy policy, bool any, bool anyNew, bool pending, bool notify, bool deploy)
        => Assert.Equal((notify, deploy), CheckImageUpdatesCommandHandler.Decide(policy, any, anyNew, pending));
}

/// <summary>The check (detection + policy), settings, tag listing and pinning handlers.</summary>
public class ImageUpdateHandlerTests
{
    private const string Old = "sha256:aaaa";
    private const string New = "sha256:bbbb";

    private readonly DockiUp.Infrastructure.DockiUpDbContext _db = TestDb.Create();
    private readonly Mock<IDockerService> _docker = new();
    private readonly Mock<IDockerServiceResolver> _resolver = new();
    private readonly Mock<IRegistryClient> _registry = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IImageUpdateEvents> _events = new();
    private readonly Mock<IActivityLogger> _activity = new();
    private readonly List<ImageUpdatesFound> _published = [];

    public ImageUpdateHandlerTests()
    {
        _resolver.Setup(r => r.Resolve(It.IsAny<Guid?>())).Returns(_docker.Object);
        _mediator.Setup(m => m.Publish(It.IsAny<ImageUpdatesFound>(), It.IsAny<CancellationToken>()))
            .Callback<ImageUpdatesFound, CancellationToken>((n, _) => _published.Add(n)).Returns(ValueTask.CompletedTask);
        _mediator.Setup(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueDeploymentCommand c, CancellationToken _) => new DeploymentDto(Guid.NewGuid(), c.ProjectId, c.Trigger,
                DeploymentStatus.Queued, c.ActorName, null, null, null, null, DateTime.UtcNow, null, null, null));
    }

    private ProjectInfo Seed(ImageUpdatePolicy policy = ImageUpdatePolicy.Notify, Guid? node = null, params string[] excluded)
    {
        var p = new ProjectInfo
        {
            ProjectName = "App", DockerProjectName = "app", ProjectOrigin = ProjectOriginType.Compose, NodeId = node,
            ProjectPath = "/p/app", ComposePath = "/p/app/dockiup_compose.yml", ProjectUpdateMethod = ProjectUpdateMethod.Manual,
            ImageUpdatePolicy = policy, ImageUpdateExcludedServices = [.. excluded],
        };
        _db.ProjectInfo.Add(p);
        _db.SaveChanges();
        return p;
    }

    private void Services(params ServiceImageDto[] services)
        => _docker.Setup(d => d.GetServiceImagesAsync("app", It.IsAny<CancellationToken>())).ReturnsAsync(services);

    private void Latest(string image, string? digest)
        => _registry.Setup(r => r.GetDigestAsync(It.Is<ImageReference>(i => i.ToString() == image), It.IsAny<CancellationToken>())).ReturnsAsync(digest);

    private CheckImageUpdatesCommandHandler Checker() => new(_db, _resolver.Object, _registry.Object, _mediator.Object, _events.Object);

    [Fact]
    public async Task Check_StoresPerServiceResults_AndSkipsWhatCannotBeCompared()
    {
        var node = Guid.NewGuid();
        var p = Seed(node: node);
        Services(
            new("web", "nginx:latest", [$"nginx@{Old}"]),
            new("cache", "redis:7", [$"redis@{New}"]),
            new("api", "app-api", []),                               // built locally
            new("pinned", $"postgres@{Old}", [$"postgres@{Old}"]),  // pinned by digest
            new("bad", "NOT A REF", []));
        Latest("nginx:latest", New);
        Latest("redis:7", New);

        var all = await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None);

        _resolver.Verify(r => r.Resolve(node)); // asks the node the project runs on
        var by = all.ToDictionary(u => u.ServiceName);
        Assert.True(by["web"].UpdateAvailable);
        Assert.Equal(Old, by["web"].CurrentDigest);
        Assert.Equal(New, by["web"].LatestDigest);
        Assert.False(by["cache"].UpdateAvailable);
        Assert.Null(by["cache"].Note);
        Assert.Contains("Built locally", by["api"].Note);
        Assert.Contains("digest", by["pinned"].Note);
        Assert.NotNull(by["bad"].Note);
        _registry.Verify(r => r.GetDigestAsync(It.IsAny<ImageReference>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _events.Verify(e => e.ChangedAsync(It.Is<ImageUpdateDto[]>(a => a.Length == 5)));

        var found = Assert.Single(_published);
        Assert.Equal(p.Id, found.ProjectId);
        Assert.Equal("web", Assert.Single(found.Updates).ServiceName);
        _mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Never); // Notify only
    }

    [Fact]
    public async Task Check_ReportsAnUpdateOnlyOnce_UntilTheTagMovesAgain()
    {
        Seed();
        Services(new ServiceImageDto("web", "nginx:latest", [$"nginx@{Old}"]));
        Latest("nginx:latest", New);

        await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None);
        await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None);
        Assert.Single(_published);

        Latest("nginx:latest", "sha256:cccc");
        await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None);
        Assert.Equal(2, _published.Count);
    }

    [Fact]
    public async Task Check_RegistryErrorsBecomeANote_NotAFailure()
    {
        Seed();
        Services(new ServiceImageDto("web", "ghcr.io/me/private:1", ["ghcr.io/me/private@" + Old]));
        _registry.Setup(r => r.GetDigestAsync(It.IsAny<ImageReference>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("ghcr.io/me/private: access denied"));

        var row = Assert.Single(await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None));
        Assert.False(row.UpdateAvailable);
        Assert.Contains("access denied", row.Note);
    }

    [Fact]
    public async Task Check_UnreachableHost_KeepsTheLastResults()
    {
        var p = Seed();
        _db.ImageUpdates.Add(new ImageUpdateStatus { ProjectId = p.Id, ServiceName = "web", Image = "nginx", UpdateAvailable = true });
        _db.SaveChanges();
        _docker.Setup(d => d.GetServiceImagesAsync("app", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("node offline"));

        var row = Assert.Single(await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None));
        Assert.True(row.UpdateAvailable);
    }

    [Fact]
    public async Task Check_Auto_QueuesAnImageUpdateDeployment_UnlessOneIsPending()
    {
        var p = Seed(ImageUpdatePolicy.Auto);
        Services(new ServiceImageDto("web", "nginx:latest", [$"nginx@{Old}"]));
        Latest("nginx:latest", New);

        await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<QueueDeploymentCommand>(c => c.ProjectId == p.Id && c.Trigger == DeploymentTrigger.ImageUpdate), It.IsAny<CancellationToken>()), Times.Once);

        _db.Deployments.Add(new Deployment { ProjectId = p.Id, Trigger = DeploymentTrigger.ImageUpdate });
        _db.SaveChanges();
        await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None);
        _mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Check_ExcludedServices_AreNotComparedOrDeployed()
    {
        Seed(ImageUpdatePolicy.Auto, null, "web");
        Services(new ServiceImageDto("web", "nginx:latest", [$"nginx@{Old}"]));
        Latest("nginx:latest", New);

        var row = Assert.Single(await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None));

        Assert.False(row.UpdateAvailable);
        Assert.Contains("Excluded", row.Note);
        _registry.VerifyNoOtherCalls();
        _mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Check_Off_ClearsResults_AndAsksNothing()
    {
        var p = Seed(ImageUpdatePolicy.Off);
        _db.ImageUpdates.Add(new ImageUpdateStatus { ProjectId = p.Id, ServiceName = "web", Image = "nginx" });
        _db.SaveChanges();

        Assert.Empty(await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None));
        _docker.Verify(d => d.GetServiceImagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Check_DropsServicesThatAreGone_AndRemovedProjects()
    {
        var p = Seed();
        _db.ImageUpdates.Add(new ImageUpdateStatus { ProjectId = p.Id, ServiceName = "old", Image = "nginx" });
        _db.ImageUpdates.Add(new ImageUpdateStatus { ProjectId = Guid.NewGuid(), ServiceName = "web", Image = "nginx" });
        _db.SaveChanges();
        Services(new ServiceImageDto("web", "nginx:latest", [$"nginx@{New}"]));
        Latest("nginx:latest", New);

        var row = Assert.Single(await Checker().Handle(new CheckImageUpdatesCommand(), CancellationToken.None));
        Assert.Equal("web", row.ServiceName);
    }

    [Fact]
    public async Task Check_UnknownProject_IsNotFound()
        => await Assert.ThrowsAsync<KeyNotFoundException>(async () => await Checker().Handle(new CheckImageUpdatesCommand(Guid.NewGuid()), CancellationToken.None));

    [Fact]
    public async Task Settings_RoundTrip_ExcludingClearsTheBadgeRightAway()
    {
        var p = Seed();
        _db.ImageUpdates.Add(new ImageUpdateStatus { ProjectId = p.Id, ServiceName = "web", Image = "nginx", UpdateAvailable = true, LatestDigest = New });
        _db.SaveChanges();
        _docker.Setup(d => d.GetImageOverridesAsync("/p/app")).ReturnsAsync(new Dictionary<string, string> { ["web"] = "nginx:1.27" });

        await new SetImageUpdateSettingsCommandHandler(_db, _activity.Object)
            .Handle(new SetImageUpdateSettingsCommand(p.Id, ImageUpdatePolicy.Auto, [" web ", "web"]), CancellationToken.None);
        var settings = await new GetImageUpdateSettingsQueryHandler(_db, _resolver.Object).Handle(new GetImageUpdateSettingsQuery(p.Id), CancellationToken.None);

        Assert.Equal(ImageUpdatePolicy.Auto, settings.Policy);
        Assert.Equal(["web"], settings.ExcludedServices);
        Assert.Equal("nginx:1.27", settings.PinnedImages["web"]);
        Assert.False((await _db.ImageUpdates.SingleAsync()).UpdateAvailable);
        _activity.Verify(a => a.LogAsync("image.policy", "App", p.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>(), null));
    }

    [Fact]
    public async Task Settings_RejectBadServiceNames()
    {
        var p = Seed();
        await Assert.ThrowsAsync<ArgumentException>(async () => await new SetImageUpdateSettingsCommandHandler(_db, _activity.Object)
            .Handle(new SetImageUpdateSettingsCommand(p.Id, ImageUpdatePolicy.Notify, ["../etc"]), CancellationToken.None));
    }

    [Fact]
    public async Task Settings_NodeOffline_StillLoadWithoutPins()
    {
        var p = Seed(node: Guid.NewGuid());
        _docker.Setup(d => d.GetImageOverridesAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("offline"));
        var settings = await new GetImageUpdateSettingsQueryHandler(_db, _resolver.Object).Handle(new GetImageUpdateSettingsQuery(p.Id), CancellationToken.None);
        Assert.Empty(settings.PinnedImages);
    }

    [Fact]
    public async Task ListTags_UsesTheServiceImage_NewestFirst()
    {
        var p = Seed();
        Services(new ServiceImageDto("web", "nginx:1.25", []));
        _registry.Setup(r => r.ListTagsAsync(It.Is<ImageReference>(i => i.Repository == "library/nginx"), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["1.25", "latest", "1.27"]);

        var tags = await new ListServiceTagsQueryHandler(_db, _resolver.Object, _registry.Object).Handle(new ListServiceTagsQuery(p.Id, "web"), CancellationToken.None);

        Assert.Equal(["latest", "1.27", "1.25"], tags);
    }

    [Fact]
    public async Task ListTags_UnknownService_IsNotFound()
    {
        var p = Seed();
        Services();
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await new ListServiceTagsQueryHandler(_db, _resolver.Object, _registry.Object).Handle(new ListServiceTagsQuery(p.Id, "web"), CancellationToken.None));
    }

    [Fact]
    public async Task Pin_WritesTheOverride_OnTheProjectsHost_AndQueuesADeployment()
    {
        var node = Guid.NewGuid();
        var p = Seed(node: node);
        _db.ImageUpdates.Add(new ImageUpdateStatus { ProjectId = p.Id, ServiceName = "web", Image = "ghcr.io/me/app:1.0" });
        _db.SaveChanges();

        var handler = new PinServiceImageCommandHandler(_db, _resolver.Object, _mediator.Object, _activity.Object);
        var queued = await handler.Handle(new PinServiceImageCommand(p.Id, "web", "1.2", "ada"), CancellationToken.None);

        _resolver.Verify(r => r.Resolve(node));
        _docker.Verify(d => d.SetImageOverrideAsync("/p/app", "web", "ghcr.io/me/app:1.2"));
        Assert.Equal(DeploymentTrigger.Manual, queued.Trigger);
        Assert.Equal("ada", queued.ActorName);

        await handler.Handle(new PinServiceImageCommand(p.Id, "web", null), CancellationToken.None);
        _docker.Verify(d => d.SetImageOverrideAsync("/p/app", "web", null));
        _activity.Verify(a => a.LogAsync("image.reset", "App", p.Id, "web", It.IsAny<CancellationToken>(), null));
    }

    [Theory]
    [InlineData("web", "bad tag")]
    [InlineData("../x", "1.0")]
    public async Task Pin_RejectsBadInput(string service, string tag)
    {
        var p = Seed();
        Services(new ServiceImageDto("web", "nginx:1.25", []));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new PinServiceImageCommandHandler(_db, _resolver.Object, _mediator.Object, _activity.Object)
                .Handle(new PinServiceImageCommand(p.Id, service, tag), CancellationToken.None));
        _docker.Verify(d => d.SetImageOverrideAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task FoundUpdates_AreRecordedInTheActivityFeed()
    {
        var id = Guid.NewGuid();
        await new ImageUpdatesFoundActivityHandler(_activity.Object).Handle(new ImageUpdatesFound(id, "App", ImageUpdatePolicy.Notify,
            [new ImageUpdateDto(id, "web", "nginx:latest", Old, New, DateTime.UtcNow, true, null)]), CancellationToken.None);
        _activity.Verify(a => a.LogAsync("image.update", "App", id, "web (nginx:latest)", It.IsAny<CancellationToken>(), null));
    }
}

/// <summary>The deploy runner's ImageUpdate path: only the changed services, no git sync.</summary>
public class ImageUpdateDeploymentTests
{
    private readonly DockiUp.Infrastructure.DockiUpDbContext _db = TestDb.Create();
    private readonly Mock<IDockerService> _docker = new();
    private readonly Mock<IDockerServiceResolver> _resolver = new();
    private readonly Mock<IDeploymentEvents> _events = new();
    private readonly Mock<IActivityLogger> _activity = new();

    public ImageUpdateDeploymentTests()
    {
        _resolver.Setup(r => r.Resolve(It.IsAny<Guid?>())).Returns(_docker.Object);
        _events.Setup(e => e.ChangedAsync(It.IsAny<DeploymentDto>())).Returns(Task.CompletedTask);
        _events.Setup(e => e.LogAsync(It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);
    }

    private (ProjectInfo, Deployment) Seed()
    {
        var p = new ProjectInfo
        {
            ProjectName = "App", DockerProjectName = "app", ProjectOrigin = ProjectOriginType.Git, Branch = "main",
            ProjectPath = "/p/app", ComposePath = "/p/app/docker-compose.yml", ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };
        _db.ProjectInfo.Add(p);
        _db.Deployments.Add(new Deployment
        {
            ProjectId = p.Id, Trigger = DeploymentTrigger.Manual, Status = DeploymentStatus.Succeeded,
            CommitAfter = "abc1234", CommitMessage = "feat", FinishedAt = DateTime.UtcNow.AddHours(-1),
        });
        var d = new Deployment { ProjectId = p.Id, Trigger = DeploymentTrigger.ImageUpdate };
        _db.Deployments.Add(d);
        _db.SaveChanges();
        return (p, d);
    }

    [Fact]
    public async Task ImageUpdate_PullsAndRecreatesOnlyChangedServices_WithoutGitSync_AndClearsTheBadge()
    {
        var (p, d) = Seed();
        _db.ImageUpdates.AddRange(
            new ImageUpdateStatus { ProjectId = p.Id, ServiceName = "web", Image = "nginx", UpdateAvailable = true, CurrentDigest = "sha256:a", LatestDigest = "sha256:b" },
            new ImageUpdateStatus { ProjectId = p.Id, ServiceName = "db", Image = "postgres" });
        _db.SaveChanges();
        ComposeTarget? target = null;
        _docker.Setup(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<ComposeTarget, Func<string, Task>, CancellationToken>((t, _, _) => target = t)
            .ReturnsAsync(new ComposeUpResult(true));

        await new DeploymentRunner(_db, _resolver.Object, _events.Object, _activity.Object)
            .RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.ImageUpdate, d.Id), CancellationToken.None);

        Assert.Equal(["web"], target!.Services!);
        _docker.Verify(x => x.SyncRepositoryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        var saved = await _db.Deployments.SingleAsync(x => x.Id == d.Id);
        Assert.Equal(DeploymentStatus.Succeeded, saved.Status);
        Assert.Equal("abc1234", saved.CommitAfter); // the deployed commit carries on
        Assert.Contains("web", saved.Log);
        var web = await _db.ImageUpdates.SingleAsync(u => u.ServiceName == "web");
        Assert.False(web.UpdateAvailable);
        Assert.Equal("sha256:b", web.CurrentDigest);
    }

    [Fact]
    public async Task ImageUpdate_NothingPending_SucceedsWithoutTouchingContainers()
    {
        var (p, d) = Seed();

        await new DeploymentRunner(_db, _resolver.Object, _events.Object, _activity.Object)
            .RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.ImageUpdate, d.Id), CancellationToken.None);

        _docker.Verify(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(DeploymentStatus.Succeeded, (await _db.Deployments.SingleAsync(x => x.Id == d.Id)).Status);
    }

    [Fact]
    public async Task OtherTriggers_DeployEveryService()
    {
        var (p, _) = Seed();
        var manual = new Deployment { ProjectId = p.Id, Trigger = DeploymentTrigger.Manual };
        _db.Deployments.Add(manual);
        _db.SaveChanges();
        _docker.Setup(x => x.SyncRepositoryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitSyncResult("a", "b", "main"));
        ComposeTarget? target = null;
        _docker.Setup(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<ComposeTarget, Func<string, Task>, CancellationToken>((t, _, _) => target = t)
            .ReturnsAsync(new ComposeUpResult(false));

        await new DeploymentRunner(_db, _resolver.Object, _events.Object, _activity.Object)
            .RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, manual.Id), CancellationToken.None);

        Assert.Null(target!.Services);
    }
}
