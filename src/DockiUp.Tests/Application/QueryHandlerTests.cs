using DockiUp.Application.Dtos;
using DockiUp.Application.Enums;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Queries;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Moq;

namespace DockiUp.Tests.Application;

/// <summary>Behavioural tests for the read-side query handlers: project/container lookup routing
/// through the resolver, DB reconciliation and fallbacks, dashboard aggregation, activity paging,
/// and app-info reporting.</summary>
public class QueryHandlerTests
{
    // ---- helpers -------------------------------------------------------------

    private static ContainerDto Container(UpdateMethodType state, string id = "c1", string name = "n1") => new()
    {
        Id = id,
        Name = name,
        Status = "Up 2 minutes",
        State = state,
        ServiceName = "svc",
        ProjectName = "proj",
    };

    private static ProjectDto Project(string dockerName, ContainerDto[]? containers = null, string desc = "daemon desc", Guid? nodeId = null) => new()
    {
        Id = null,
        ProjectName = dockerName,
        DockerProjectName = dockerName,
        ProjectDescription = desc,
        ManagedByDockiUp = false,
        NodeId = nodeId,
        Containers = containers ?? [],
    };

    private static ProjectInfo SeedProject(
        DockiUp.Infrastructure.DockiUpDbContext db,
        string dockerName = "web",
        Guid? nodeId = null,
        string? description = null,
        string path = "/p",
        ProjectUpdateMethod updateMethod = ProjectUpdateMethod.Manual)
    {
        var p = new ProjectInfo
        {
            ProjectName = "Proj " + dockerName,
            DockerProjectName = dockerName,
            Description = description,
            ProjectOrigin = ProjectOriginType.Compose,
            ProjectPath = path,
            ComposePath = path + "/dockiup_compose.yml",
            ProjectUpdateMethod = updateMethod,
            NodeId = nodeId,
        };
        db.ProjectInfo.Add(p);
        db.SaveChanges();
        return p;
    }

    private static ActivityEntry SeedActivity(
        DockiUp.Infrastructure.DockiUpDbContext db,
        string action,
        DateTime createdAt,
        string target = "tgt",
        Guid? projectId = null,
        string? details = null,
        string? actor = null)
    {
        var e = new ActivityEntry
        {
            Action = action,
            Target = target,
            ProjectId = projectId,
            Details = details,
            ActorName = actor,
            CreatedAt = createdAt,
        };
        db.ActivityEntries.Add(e);
        db.SaveChanges();
        return e;
    }

    // ---- GetProjectQuery -----------------------------------------------------

    [Fact]
    public async Task GetProject_ById_DaemonHasMatch_ReconcilesDbFieldsOntoDaemonDto()
    {
        var db = TestDb.Create();
        var node = Guid.NewGuid();
        var p = SeedProject(db, dockerName: "web", nodeId: node, description: "Custom Desc", path: "/n");

        var byName = Project("web", containers: [Container(UpdateMethodType.Running)], desc: "daemon desc", nodeId: null);
        var nodeDocker = new Mock<IDockerService>();
        nodeDocker.Setup(d => d.GetProjectByDockerNameAsync("web")).ReturnsAsync(byName);
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(nodeDocker.Object);
        var localDocker = new Mock<IDockerService>();

        var result = await new GetProjectQueryHandler(localDocker.Object, resolver.Object, db)
            .Handle(new GetProjectQuery(p.Id), CancellationToken.None);

        Assert.NotNull(result);
        // Reconciled from DB row.
        Assert.Equal(p.Id, result!.Id);
        Assert.Equal("Proj web", result.ProjectName);
        Assert.Equal("Custom Desc", result.ProjectDescription);
        Assert.True(result.ManagedByDockiUp);
        Assert.Equal(node, result.NodeId);
        Assert.Equal("/n", result.ProjectPath);
        Assert.Equal("Manual", result.UpdateMethod);
        // Daemon-provided containers preserved (proves the daemon dto was used, not the fallback).
        Assert.Single(result.Containers);
        resolver.Verify(r => r.Resolve(node), Times.Once);
        nodeDocker.Verify(d => d.GetProjectByDockerNameAsync("web"), Times.Once);
        localDocker.Verify(d => d.GetProjectByDockerNameAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetProject_ById_NullDbDescription_KeepsDaemonDescription()
    {
        var db = TestDb.Create();
        var p = SeedProject(db, dockerName: "web", nodeId: null, description: null);

        var byName = Project("web", desc: "daemon desc");
        var localDocker = new Mock<IDockerService>();
        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.GetProjectByDockerNameAsync("web")).ReturnsAsync(byName);
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(null)).Returns(docker.Object);

        var result = await new GetProjectQueryHandler(localDocker.Object, resolver.Object, db)
            .Handle(new GetProjectQuery(p.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("daemon desc", result!.ProjectDescription);
        resolver.Verify(r => r.Resolve(null), Times.Once);
    }

    [Fact]
    public async Task GetProject_ById_DaemonNoMatch_BuildsFallbackDto()
    {
        var db = TestDb.Create();
        var node = Guid.NewGuid();
        var p = SeedProject(db, dockerName: "web", nodeId: node, description: null, path: "/n",
            updateMethod: ProjectUpdateMethod.Periodically);

        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.GetProjectByDockerNameAsync("web")).ReturnsAsync((ProjectDto?)null);
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(docker.Object);
        var localDocker = new Mock<IDockerService>();

        var result = await new GetProjectQueryHandler(localDocker.Object, resolver.Object, db)
            .Handle(new GetProjectQuery(p.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(p.Id, result!.Id);
        Assert.Equal("Proj web", result.ProjectName);
        Assert.Equal("web", result.DockerProjectName);
        Assert.Equal("", result.ProjectDescription); // null Description => ""
        Assert.True(result.ManagedByDockiUp);
        Assert.Equal(node, result.NodeId);
        Assert.Empty(result.Containers);
        Assert.Equal("/n", result.ProjectPath);
        Assert.Equal("Periodically", result.UpdateMethod);
    }

    [Fact]
    public async Task GetProject_ById_NotFound_ReturnsNull_AndDoesNotResolve()
    {
        var db = TestDb.Create();
        var resolver = new Mock<IDockerServiceResolver>();
        var localDocker = new Mock<IDockerService>();

        var result = await new GetProjectQueryHandler(localDocker.Object, resolver.Object, db)
            .Handle(new GetProjectQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result);
        resolver.Verify(r => r.Resolve(It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task GetProject_ByDockerName_UsesLocalDocker_NotResolver()
    {
        var db = TestDb.Create();
        var dto = Project("web", containers: [Container(UpdateMethodType.Running)]);
        var localDocker = new Mock<IDockerService>();
        localDocker.Setup(d => d.GetProjectByDockerNameAsync("web")).ReturnsAsync(dto);
        var resolver = new Mock<IDockerServiceResolver>();

        var result = await new GetProjectQueryHandler(localDocker.Object, resolver.Object, db)
            .Handle(new GetProjectQuery(null, "web"), CancellationToken.None);

        Assert.Same(dto, result);
        localDocker.Verify(d => d.GetProjectByDockerNameAsync("web"), Times.Once);
        resolver.Verify(r => r.Resolve(It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task GetProject_ByDockerName_LocalReturnsNull_ReturnsNull()
    {
        var db = TestDb.Create();
        var localDocker = new Mock<IDockerService>();
        localDocker.Setup(d => d.GetProjectByDockerNameAsync("missing")).ReturnsAsync((ProjectDto?)null);
        var resolver = new Mock<IDockerServiceResolver>();

        var result = await new GetProjectQueryHandler(localDocker.Object, resolver.Object, db)
            .Handle(new GetProjectQuery(null, "missing"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetProject_NoIdentifiers_ReturnsNull()
    {
        var db = TestDb.Create();
        var localDocker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();

        var result = await new GetProjectQueryHandler(localDocker.Object, resolver.Object, db)
            .Handle(new GetProjectQuery(null, null), CancellationToken.None);

        Assert.Null(result);
        localDocker.Verify(d => d.GetProjectByDockerNameAsync(It.IsAny<string>()), Times.Never);
        resolver.Verify(r => r.Resolve(It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task GetProject_BlankDockerName_ReturnsNull()
    {
        var db = TestDb.Create();
        var localDocker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();

        var result = await new GetProjectQueryHandler(localDocker.Object, resolver.Object, db)
            .Handle(new GetProjectQuery(null, "   "), CancellationToken.None);

        Assert.Null(result);
        localDocker.Verify(d => d.GetProjectByDockerNameAsync(It.IsAny<string>()), Times.Never);
    }

    // ---- GetContainerQuery ---------------------------------------------------

    [Fact]
    public async Task GetContainer_ReturnsInspected_RoutesToNode()
    {
        var node = Guid.NewGuid();
        var inspected = Container(UpdateMethodType.Running, id: "cid", name: "web");
        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.InspectContainerAsync("cid", It.IsAny<CancellationToken>())).ReturnsAsync(inspected);
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(docker.Object);

        var result = await new GetContainerQueryHandler(resolver.Object)
            .Handle(new GetContainerQuery("cid", node), CancellationToken.None);

        Assert.Same(inspected, result);
        resolver.Verify(r => r.Resolve(node), Times.Once);
        docker.Verify(d => d.InspectContainerAsync("cid", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContainer_RoutesLocally_WhenNodeNull()
    {
        var inspected = Container(UpdateMethodType.Stopped, id: "cid");
        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.InspectContainerAsync("cid", It.IsAny<CancellationToken>())).ReturnsAsync(inspected);
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(null)).Returns(docker.Object);

        var result = await new GetContainerQueryHandler(resolver.Object)
            .Handle(new GetContainerQuery("cid"), CancellationToken.None);

        Assert.Same(inspected, result);
        resolver.Verify(r => r.Resolve(null), Times.Once);
    }

    [Fact]
    public async Task GetContainer_NullInspect_ThrowsKeyNotFound()
    {
        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.InspectContainerAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((ContainerDto?)null);
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(null)).Returns(docker.Object);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new GetContainerQueryHandler(resolver.Object)
                .Handle(new GetContainerQuery("ghost"), CancellationToken.None).AsTask());
        Assert.Contains("ghost", ex.Message);
    }

    // ---- GetContainerLogsQuery ----------------------------------------------

    [Fact]
    public async Task GetContainerLogs_PassesTail_RoutesToNode()
    {
        var node = Guid.NewGuid();
        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.GetContainerLogsAsync("cid", new ContainerLogOptions(50, true, true, false), It.IsAny<CancellationToken>())).ReturnsAsync("log output");
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(docker.Object);

        var result = await new GetContainerLogsQueryHandler(resolver.Object)
            .Handle(new GetContainerLogsQuery("cid", 50, node), CancellationToken.None);

        Assert.Equal("log output", result);
        resolver.Verify(r => r.Resolve(node), Times.Once);
        docker.Verify(d => d.GetContainerLogsAsync("cid", new ContainerLogOptions(50, true, true, false), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContainerLogs_NullTail_RoutesLocally()
    {
        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.GetContainerLogsAsync("cid", new ContainerLogOptions(null, true, true, false), It.IsAny<CancellationToken>())).ReturnsAsync("");
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(null)).Returns(docker.Object);

        var result = await new GetContainerLogsQueryHandler(resolver.Object)
            .Handle(new GetContainerLogsQuery("cid"), CancellationToken.None);

        Assert.Equal("", result);
        resolver.Verify(r => r.Resolve(null), Times.Once);
        docker.Verify(d => d.GetContainerLogsAsync("cid", new ContainerLogOptions(null, true, true, false), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- GetDashboardStatsQuery ---------------------------------------------

    [Fact]
    public async Task GetDashboardStats_AggregatesCounts_AndTakesFiveNewestActivity()
    {
        var db = TestDb.Create();
        SeedProject(db, dockerName: "a");
        SeedProject(db, dockerName: "b");

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 1; i <= 7; i++)
            SeedActivity(db, action: $"act{i}", createdAt: baseTime.AddMinutes(i));

        var projectA = Project("a", containers: [Container(UpdateMethodType.Running, "a1"), Container(UpdateMethodType.Stopped, "a2")]);
        var projectB = Project("b", containers: [Container(UpdateMethodType.Running, "b1")]);
        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.GetProjectsAsync()).ReturnsAsync(new[] { projectA, projectB });

        var stats = await new GetDashboardStatsQueryHandler(docker.Object, db)
            .Handle(new GetDashboardStatsQuery(), CancellationToken.None);

        Assert.Equal(2, stats.TotalProjects);
        Assert.Equal(3, stats.TotalContainers);
        Assert.Equal(2, stats.RunningContainers);
        Assert.Equal(5, stats.RecentActivity.Count);
        // Newest first.
        Assert.Equal("act7", stats.RecentActivity[0].Action);
        var times = stats.RecentActivity.Select(a => a.CreatedAt).ToList();
        Assert.Equal(times.OrderByDescending(t => t).ToList(), times);
        Assert.Equal(baseTime.AddMinutes(7), stats.RecentActivity[0].CreatedAt);
    }

    [Fact]
    public async Task GetDashboardStats_EmptyState_ReturnsZeros()
    {
        var db = TestDb.Create();
        var docker = new Mock<IDockerService>();
        docker.Setup(d => d.GetProjectsAsync()).ReturnsAsync(Array.Empty<ProjectDto>());

        var stats = await new GetDashboardStatsQueryHandler(docker.Object, db)
            .Handle(new GetDashboardStatsQuery(), CancellationToken.None);

        Assert.Equal(0, stats.TotalProjects);
        Assert.Equal(0, stats.TotalContainers);
        Assert.Equal(0, stats.RunningContainers);
        Assert.Empty(stats.RecentActivity);
    }

    // ---- ListActivityQuery ---------------------------------------------------

    [Fact]
    public async Task ListActivity_OrdersNewestFirst_AndMapsAllFields()
    {
        var db = TestDb.Create();
        var baseTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        SeedActivity(db, "old", baseTime.AddMinutes(1));
        var proj = Guid.NewGuid();
        var newest = SeedActivity(db, "deploy", baseTime.AddMinutes(3), target: "My App",
            projectId: proj, details: "Compose", actor: "niclas");
        SeedActivity(db, "mid", baseTime.AddMinutes(2));

        var result = await new ListActivityQueryHandler(db)
            .Handle(new ListActivityQuery(), CancellationToken.None);

        Assert.Equal(3, result.Count);
        var first = result[0];
        Assert.Equal(newest.Id, first.Id);
        Assert.Equal("deploy", first.Action);
        Assert.Equal("My App", first.Target);
        Assert.Equal(proj, first.ProjectId);
        Assert.Equal("Compose", first.Details);
        Assert.Equal("niclas", first.ActorName);
        Assert.Equal(baseTime.AddMinutes(3), first.CreatedAt);
        // Whole sequence is descending by CreatedAt.
        var times = result.Select(a => a.CreatedAt).ToList();
        Assert.Equal(times.OrderByDescending(t => t).ToList(), times);
    }

    [Fact]
    public async Task ListActivity_RespectsLimit()
    {
        var db = TestDb.Create();
        var baseTime = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 1; i <= 5; i++)
            SeedActivity(db, $"a{i}", baseTime.AddMinutes(i));

        var result = await new ListActivityQueryHandler(db)
            .Handle(new ListActivityQuery(2), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("a5", result[0].Action);
        Assert.Equal("a4", result[1].Action);
    }

    [Fact]
    public async Task ListActivity_ClampsLimitToAtLeastOne()
    {
        var db = TestDb.Create();
        var baseTime = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        SeedActivity(db, "a1", baseTime.AddMinutes(1));
        SeedActivity(db, "a2", baseTime.AddMinutes(2));
        SeedActivity(db, "a3", baseTime.AddMinutes(3));

        var result = await new ListActivityQueryHandler(db)
            .Handle(new ListActivityQuery(0), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("a3", result[0].Action); // clamp(0)->1, newest only
    }

    [Fact]
    public async Task ListActivity_HugeLimit_ClampsAndReturnsAllRows()
    {
        var db = TestDb.Create();
        var baseTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        SeedActivity(db, "a1", baseTime.AddMinutes(1));
        SeedActivity(db, "a2", baseTime.AddMinutes(2));

        var result = await new ListActivityQueryHandler(db)
            .Handle(new ListActivityQuery(5000), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ListActivity_Empty_ReturnsEmpty()
    {
        var db = TestDb.Create();
        var result = await new ListActivityQueryHandler(db)
            .Handle(new ListActivityQuery(), CancellationToken.None);
        Assert.Empty(result);
    }

    // ---- GetAppInfoQuery -----------------------------------------------------

    [Fact]
    public async Task GetAppInfo_ReturnsVersionPrefixedAndNonEmptyEnvironment()
    {
        var result = await new GetAppInfoQueryHandler().Handle(new GetAppInfoQuery(), CancellationToken.None);

        Assert.StartsWith("v", result.Version);
        Assert.False(string.IsNullOrWhiteSpace(result.Environment));
    }

    [Fact]
    public async Task GetAppInfo_ReflectsAspNetCoreEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "UnitTestEnv");
            var result = await new GetAppInfoQueryHandler().Handle(new GetAppInfoQuery(), CancellationToken.None);
            Assert.Equal("UnitTestEnv", result.Environment);
            Assert.StartsWith("v", result.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", original);
        }
    }
}
