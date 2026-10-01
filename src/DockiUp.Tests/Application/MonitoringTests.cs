using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Monitoring;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DockiUp.Tests.Application;

/// <summary>Stats history downsampling/retention, the live store, the cleanup schedule's due rule and keep-rule,
/// and the monitoring/housekeeping handlers.</summary>
public class MonitoringTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ContainerStatsDto Sample(string id, DateTime at, double cpu, long mem, Guid? node = null, long rx = 0)
        => new(id, id + "-name", "proj", cpu, mem, 1000, rx, 0, at, node);

    // ---- Downsampling ----

    [Fact]
    public void Downsampler_AveragesAMinute_EmitsItWhenTheNextMinuteStarts()
    {
        var d = new StatsDownsampler();
        Assert.Empty(d.Add([Sample("a", T0.AddSeconds(5), 10, 100, rx: 1)]));
        Assert.Empty(d.Add([Sample("a", T0.AddSeconds(20), 30, 300, rx: 5)]));

        var done = Assert.Single(d.Add([Sample("a", T0.AddSeconds(65), 99, 999)]));

        Assert.Equal(T0, done.Timestamp);
        Assert.Equal(20, done.CpuPercent);
        Assert.Equal(200, done.MemoryUsage);
        Assert.Equal(5, done.NetworkRx); // cumulative counter: latest reading
        Assert.Equal(("a", "a-name", "proj"), (done.ContainerId, done.ContainerName, done.ProjectName));
    }

    [Fact]
    public void Downsampler_ClosesStaleBuckets_OfContainersThatStopped()
    {
        var d = new StatsDownsampler();
        d.Add([Sample("gone", T0, 50, 500), Sample("a", T0, 1, 1)]);
        d.Add([Sample("a", T0.AddMinutes(1), 1, 1)]); // "gone" sent nothing, but it's only one minute old

        var done = d.Add([Sample("a", T0.AddMinutes(2), 1, 1)]);

        Assert.Contains(done, p => p.ContainerId == "gone" && p.Timestamp == T0);
        Assert.Contains(done, p => p.ContainerId == "a" && p.Timestamp == T0.AddMinutes(1));
    }

    [Fact]
    public void Downsampler_KeepsHostsApart()
    {
        var d = new StatsDownsampler();
        var node = Guid.NewGuid();
        d.Add([Sample("a", T0, 10, 0), Sample("a", T0, 90, 0, node)]);
        var done = d.Add([Sample("a", T0.AddMinutes(1), 0, 0), Sample("a", T0.AddMinutes(1), 0, 0, node)]);
        Assert.Equal(10, done.Single(p => p.NodeId is null).CpuPercent);
        Assert.Equal(90, done.Single(p => p.NodeId == node).CpuPercent);
    }

    [Fact]
    public void MinuteOf_TruncatesToTheMinute()
        => Assert.Equal(T0, StatsDownsampler.MinuteOf(T0.AddSeconds(59).AddMilliseconds(999)));

    [Fact]
    public async Task History_Persists_AndPrunesPointsOlderThanRetention()
    {
        using var db = TestDb.Create();
        db.ContainerStatSamples.Add(new ContainerStatSample { ContainerId = "a", ContainerName = "a", ProjectName = "p", Timestamp = T0.AddHours(-25) });
        db.ContainerStatSamples.Add(new ContainerStatSample { ContainerId = "a", ContainerName = "a", ProjectName = "p", Timestamp = T0.AddHours(-23) });
        await db.SaveChangesAsync();

        await ContainerStatsHistory.PersistAsync(db, [new ContainerStatSample { ContainerId = "a", ContainerName = "a", ProjectName = "p", Timestamp = T0 }], T0, TimeSpan.FromHours(24));

        var left = await db.ContainerStatSamples.Select(x => x.Timestamp).OrderBy(x => x).ToListAsync();
        Assert.Equal([T0.AddHours(-23), T0], left);
    }

    // ---- Live store ----

    [Fact]
    public void Store_IngestReplacesAHostsValues_ForgetDropsThem()
    {
        var store = new ContainerStatsStore();
        var node = Guid.NewGuid();
        store.Ingest(null, [Sample("a", T0, 1, 1), Sample("b", T0, 1, 1)]);
        store.Ingest(node, [Sample("n", T0, 1, 1)]);

        store.Ingest(null, [Sample("a", T0.AddSeconds(15), 2, 2)]); // b stopped

        Assert.Equal(["a", "n"], store.Latest.Select(s => s.ContainerId).Order());
        Assert.Equal(node, store.Latest.Single(s => s.ContainerId == "n").NodeId);
        store.Forget(node);
        Assert.Equal("a", Assert.Single(store.Latest).ContainerId);
    }

    [Fact]
    public async Task GetContainerStats_FiltersByProjectContainerAndHost()
    {
        var store = new ContainerStatsStore();
        var node = Guid.NewGuid();
        store.Ingest(null, [Sample("abc123", T0, 1, 1), new ContainerStatsDto("other", "o", "elsewhere", 0, 0, 0, 0, 0, T0)]);
        store.Ingest(node, [Sample("nodec", T0, 1, 1)]);
        var handler = new GetContainerStatsQueryHandler(store);

        Assert.Equal(3, (await handler.Handle(new GetContainerStatsQuery(), default)).Length);
        Assert.Equal("abc123", Assert.Single(await handler.Handle(new GetContainerStatsQuery("PROJ"), default)).ContainerId);
        Assert.Equal("nodec", Assert.Single(await handler.Handle(new GetContainerStatsQuery("proj", NodeId: node), default)).ContainerId);
        Assert.Equal("abc123", Assert.Single(await handler.Handle(new GetContainerStatsQuery(ContainerId: "abc"), default)).ContainerId);
    }

    [Fact]
    public async Task GetContainerStatsHistory_ReturnsTheRangeOfOneContainerOnOneHost()
    {
        using var db = TestDb.Create();
        var now = DateTime.UtcNow;
        var node = Guid.NewGuid();
        db.ContainerStatSamples.AddRange(
            new ContainerStatSample { ContainerId = "x1", ContainerName = "web", ProjectName = "p", Timestamp = now.AddMinutes(-30), CpuPercent = 1 },
            new ContainerStatSample { ContainerId = "x2", ContainerName = "web", ProjectName = "p", Timestamp = now.AddMinutes(-5), CpuPercent = 2 }, // recreated: new id, same name
            new ContainerStatSample { ContainerId = "x0", ContainerName = "web", ProjectName = "p", Timestamp = now.AddHours(-2) },
            new ContainerStatSample { ContainerId = "y", ContainerName = "web", ProjectName = "p", NodeId = node, Timestamp = now.AddMinutes(-1) },
            new ContainerStatSample { ContainerId = "z", ContainerName = "db", ProjectName = "p", Timestamp = now.AddMinutes(-1) });
        await db.SaveChangesAsync();
        var handler = new GetContainerStatsHistoryQueryHandler(db);

        var points = await handler.Handle(new GetContainerStatsHistoryQuery("web", null, 1), default);

        Assert.Equal([1d, 2d], points.Select(p => p.CpuPercent));
        Assert.Equal(3, (await handler.Handle(new GetContainerStatsHistoryQuery("web", null, 999), default)).Length); // clamped to 24h
        Assert.Single(await handler.Handle(new GetContainerStatsHistoryQuery("web", node), default));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new GetContainerStatsHistoryQuery(" "), default).AsTask());
    }

    // ---- Schedule due rule ----

    private static CleanupSchedule Schedule(CleanupFrequency f, int hour, int day = 0, DateTime? saved = null, DateTime? lastRun = null)
        => new() { Frequency = f, HourUtc = hour, DayOfWeekUtc = day, SavedAt = saved ?? T0.AddDays(-30), LastRunAt = lastRun };

    [Fact]
    public void Due_Off_Never()
        => Assert.False(CleanupScheduleRules.IsDue(Schedule(CleanupFrequency.Off, 3), T0));

    [Fact]
    public void Due_Daily_OncePerDay_AfterTheHour()
    {
        // T0 is 12:00; a 03:00 daily slot passed today.
        Assert.True(CleanupScheduleRules.IsDue(Schedule(CleanupFrequency.Daily, 3), T0));
        Assert.False(CleanupScheduleRules.IsDue(Schedule(CleanupFrequency.Daily, 3, lastRun: T0.Date.AddHours(3).AddMinutes(1)), T0));
        Assert.True(CleanupScheduleRules.IsDue(Schedule(CleanupFrequency.Daily, 3, lastRun: T0.AddDays(-1)), T0));
        // 13:00 hasn't come yet today, and yesterday's 13:00 was already run.
        Assert.False(CleanupScheduleRules.IsDue(Schedule(CleanupFrequency.Daily, 13, lastRun: T0.AddDays(-1).AddHours(1).AddMinutes(1)), T0));
    }

    [Fact]
    public void Due_NotCaughtUp_ForSlotsBeforeTheScheduleWasSaved()
    {
        Assert.False(CleanupScheduleRules.IsDue(Schedule(CleanupFrequency.Daily, 3, saved: T0.AddHours(-1)), T0));
        Assert.True(CleanupScheduleRules.IsDue(Schedule(CleanupFrequency.Daily, 3, saved: T0.AddHours(-1)), T0.AddDays(1).Date.AddHours(3)));
    }

    [Fact]
    public void Due_Weekly_OnTheChosenDay()
    {
        // 2026-10-01 is a Thursday (4).
        Assert.Equal(DayOfWeek.Thursday, T0.DayOfWeek);
        Assert.Equal(T0.Date.AddHours(3), CleanupScheduleRules.LastOccurrence(Schedule(CleanupFrequency.Weekly, 3, day: 4), T0));
        Assert.Equal(T0.Date.AddDays(-3).AddHours(3), CleanupScheduleRules.LastOccurrence(Schedule(CleanupFrequency.Weekly, 3, day: 1), T0));
        // Thursday 13:00 is still ahead -> last week's.
        Assert.Equal(T0.Date.AddDays(-7).AddHours(13), CleanupScheduleRules.LastOccurrence(Schedule(CleanupFrequency.Weekly, 13, day: 4), T0));
        Assert.False(CleanupScheduleRules.IsDue(Schedule(CleanupFrequency.Weekly, 3, day: 1, lastRun: T0.AddDays(-2)), T0));
    }

    [Fact]
    public void ScheduledRequest_KeepsProjectContainers_VolumesOnlyWhenOptedIn()
    {
        var r = CleanupScheduleRules.ScheduledRequest(Schedule(CleanupFrequency.Daily, 3));
        Assert.Equal(new PruneRequest(Containers: true, Images: true, AllImages: true, Networks: true, Volumes: false, KeepProjectContainers: true), r);
        Assert.True(CleanupScheduleRules.ScheduledRequest(new CleanupSchedule { PruneVolumes = true }).Volumes);
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void FormatBytes_IsReadable(long bytes, string expected)
        => Assert.Equal(expected, CleanupScheduleRules.FormatBytes(bytes));

    // ---- Handlers ----

    private static (Mock<IDockerServiceResolver>, Mock<IDockerService>) Resolver(Guid? node)
    {
        var docker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(docker.Object);
        return (resolver, docker);
    }

    [Fact]
    public async Task RunCleanup_PrunesWithTheKeepRule_RecordsRunAndActivity()
    {
        using var db = TestDb.Create();
        var node = new Node { Name = "brave-otter" };
        db.Nodes.Add(node);
        db.CleanupSchedules.Add(new CleanupSchedule { NodeId = node.Id, Frequency = CleanupFrequency.Daily, PruneVolumes = true });
        await db.SaveChangesAsync();
        var (resolver, docker) = Resolver(node.Id);
        docker.Setup(d => d.PruneAsync(It.IsAny<PruneRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PruneResultDto(1, 2, 3, 4, 2048));
        var activity = new Mock<IActivityLogger>();

        var result = await new RunCleanupCommandHandler(resolver.Object, db, activity.Object).Handle(new RunCleanupCommand(node.Id, Scheduled: true), default);

        Assert.Equal(2048, result.SpaceReclaimed);
        docker.Verify(d => d.PruneAsync(It.Is<PruneRequest>(r => r.KeepProjectContainers && r.Volumes && r.AllImages), It.IsAny<CancellationToken>()));
        var schedule = await db.CleanupSchedules.SingleAsync();
        Assert.NotNull(schedule.LastRunAt);
        Assert.Contains("freed 2 KB", schedule.LastRunResult);
        activity.Verify(a => a.LogAsync("cleanup", "brave-otter", null, It.Is<string>(s => s.StartsWith("Scheduled: freed 2 KB")), It.IsAny<CancellationToken>(), null));
    }

    [Fact]
    public async Task RunCleanup_PublishesACleanupReport_ForNotifications()
    {
        using var db = TestDb.Create();
        var (resolver, docker) = Resolver(null);
        docker.Setup(d => d.PruneAsync(It.IsAny<PruneRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PruneResultDto(0, 2, 0, 1, 4096));
        var publisher = new Mock<Mediator.IPublisher>();
        DockiUp.Application.Notifications.CleanupCompleted? published = null;
        publisher.Setup(p => p.Publish(It.IsAny<DockiUp.Application.Notifications.CleanupCompleted>(), It.IsAny<CancellationToken>()))
            .Callback<DockiUp.Application.Notifications.CleanupCompleted, CancellationToken>((e, _) => published = e)
            .Returns(ValueTask.CompletedTask);

        await new RunCleanupCommandHandler(resolver.Object, db, new Mock<IActivityLogger>().Object, publisher.Object)
            .Handle(new RunCleanupCommand(), default);

        Assert.NotNull(published);
        Assert.Equal(4096, published!.ReclaimedBytes);
        Assert.Contains("freed 4 KB", published.Summary);
    }

    [Fact]
    public async Task RunCleanup_Failure_IsRecordedAndLogged_ThenRethrown()
    {
        using var db = TestDb.Create();
        var (resolver, docker) = Resolver(null);
        docker.Setup(d => d.PruneAsync(It.IsAny<PruneRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("daemon down"));
        var activity = new Mock<IActivityLogger>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RunCleanupCommandHandler(resolver.Object, db, activity.Object).Handle(new RunCleanupCommand(), default).AsTask());

        var schedule = await db.CleanupSchedules.SingleAsync(); // created on demand for the local host
        Assert.Equal("failed: daemon down", schedule.LastRunResult);
        docker.Verify(d => d.PruneAsync(It.Is<PruneRequest>(r => !r.Volumes && r.KeepProjectContainers), It.IsAny<CancellationToken>()));
        activity.Verify(a => a.LogAsync("cleanup.failed", "Local host", null, "daemon down", It.IsAny<CancellationToken>(), null));
    }

    [Fact]
    public async Task SaveCleanupSchedule_Upserts_AndValidates()
    {
        using var db = TestDb.Create();
        var handler = new SaveCleanupScheduleCommandHandler(db);

        var saved = await handler.Handle(new SaveCleanupScheduleCommand(null, CleanupFrequency.Weekly, 4, 1, true), default);
        await handler.Handle(new SaveCleanupScheduleCommand(null, CleanupFrequency.Daily, 5, 0, false), default);

        Assert.Equal(CleanupFrequency.Weekly, saved.Frequency);
        var row = await db.CleanupSchedules.SingleAsync();
        Assert.Equal((CleanupFrequency.Daily, 5, false), (row.Frequency, row.HourUtc, row.PruneVolumes));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new SaveCleanupScheduleCommand(null, CleanupFrequency.Daily, 24, 0, false), default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new SaveCleanupScheduleCommand(null, CleanupFrequency.Weekly, 1, 7, false), default).AsTask());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new SaveCleanupScheduleCommand(Guid.NewGuid(), CleanupFrequency.Daily, 1, 0, false), default).AsTask());
    }

    [Fact]
    public async Task GetCleanupSchedule_DefaultsToOff()
    {
        using var db = TestDb.Create();
        var dto = await new GetCleanupScheduleQueryHandler(db).Handle(new GetCleanupScheduleQuery(), default);
        Assert.Equal(new CleanupScheduleDto(null, CleanupFrequency.Off, 3, 0, false, null, null), dto);
    }

    [Fact]
    public async Task PruneAndRemove_RouteToTheHost_AndLogActivity()
    {
        using var db = TestDb.Create();
        var (resolver, docker) = Resolver(null);
        docker.Setup(d => d.PruneAsync(It.IsAny<PruneRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PruneResultDto(0, 3, 0, 0, 10));
        var activity = new Mock<IActivityLogger>();
        var request = new PruneRequest(Images: true);

        var result = await new PruneResourcesCommandHandler(resolver.Object, db, activity.Object).Handle(new PruneResourcesCommand(request), default);
        await new RemoveResourceCommandHandler(resolver.Object, db, activity.Object).Handle(new RemoveResourceCommand(ResourceKind.Image, "sha256:1", null, "web:1"), default);

        Assert.Equal(3, result.ImagesDeleted);
        docker.Verify(d => d.PruneAsync(request, It.IsAny<CancellationToken>()));
        docker.Verify(d => d.RemoveResourceAsync(ResourceKind.Image, "sha256:1", It.IsAny<CancellationToken>()));
        activity.Verify(a => a.LogAsync("prune", "Local host", null, It.Is<string>(s => s.Contains("3 images")), It.IsAny<CancellationToken>(), null));
        activity.Verify(a => a.LogAsync("image.remove", "web:1", null, "Local host", It.IsAny<CancellationToken>(), null));
    }

    [Fact]
    public async Task GetResources_RoutesToTheNode()
    {
        var node = Guid.NewGuid();
        var (resolver, docker) = Resolver(node);
        var dto = new DockerResourcesDto([], [], [], new DiskUsageDto(1, 0, 0, 0, 0, 0));
        docker.Setup(d => d.GetResourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        Assert.Same(dto, await new GetResourcesQueryHandler(resolver.Object).Handle(new GetResourcesQuery(node), default));
    }
}
