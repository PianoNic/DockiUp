using DockiUp.Application.Dtos;
using DockiUp.Application.Enums;
using DockiUp.Application.Mappers;
using DockiUp.Application.Models;
using DockiUp.Domain;
using DockiUp.Domain.Enums;

namespace DockiUp.Tests;

/// <summary>Covers the leaf types: the raw-docker-state mapper (every switch branch), the
/// <see cref="BaseEntity"/> defaults, the domain entities' required-member round-trips, the enum
/// member values, the Application models, and every DTO constructor. These are pure/POCO tests, so
/// they assert concrete values and behaviour rather than merely "does not throw".</summary>
public class DtosDomainTests
{
    // ---------------------------------------------------------------------
    // ContainerStateMapper.ToEnum — every switch case + trimming/casing + null
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("created", UpdateMethodType.Created)]
    [InlineData("running", UpdateMethodType.Running)]
    [InlineData("restarting", UpdateMethodType.Running)]
    [InlineData("removing", UpdateMethodType.Running)]
    [InlineData("paused", UpdateMethodType.Stopped)]
    [InlineData("exited", UpdateMethodType.Stopped)]
    [InlineData("dead", UpdateMethodType.Crashed)]
    public void ToEnum_MapsEachKnownDockerState(string raw, UpdateMethodType expected)
    {
        Assert.Equal(expected, raw.ToEnum());
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    [InlineData("startingup")]
    public void ToEnum_UnrecognizedState_MapsToUnknown(string raw)
    {
        Assert.Equal(UpdateMethodType.Unknown, raw.ToEnum());
    }

    [Fact]
    public void ToEnum_Null_MapsToUnknown()
    {
        string? raw = null;
        Assert.Equal(UpdateMethodType.Unknown, raw!.ToEnum());
    }

    [Theory]
    [InlineData("  RUNNING  ", UpdateMethodType.Running)]
    [InlineData("Created", UpdateMethodType.Created)]
    [InlineData("\tDead\n", UpdateMethodType.Crashed)]
    [InlineData("ExItEd", UpdateMethodType.Stopped)]
    public void ToEnum_IsCaseInsensitiveAndTrims(string raw, UpdateMethodType expected)
    {
        Assert.Equal(expected, raw.ToEnum());
    }

    // ---------------------------------------------------------------------
    // BaseEntity defaults
    // ---------------------------------------------------------------------

    [Fact]
    public void BaseEntity_NewInstance_HasNonEmptyIdAndUtcTimestamps()
    {
        var before = DateTime.UtcNow;
        var entity = new BaseEntity();
        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.InRange(entity.CreatedAt, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.InRange(entity.UpdatedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void BaseEntity_TwoInstances_GetDistinctIds()
    {
        var a = new BaseEntity();
        var b = new BaseEntity();
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void BaseEntity_UpdatedAt_IsSettable()
    {
        var entity = new BaseEntity();
        var stamp = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        entity.UpdatedAt = stamp;
        Assert.Equal(stamp, entity.UpdatedAt);
    }

    // ---------------------------------------------------------------------
    // Domain entities — required-member round-trip + BaseEntity inheritance
    // ---------------------------------------------------------------------

    [Fact]
    public void ProjectInfo_RoundTripsValues_AndIsBaseEntity()
    {
        var node = Guid.NewGuid();
        var last = DateTime.UtcNow;
        var p = new ProjectInfo
        {
            ProjectName = "MyApp",
            DockerProjectName = "myapp",
            Description = "desc",
            ProjectOrigin = ProjectOriginType.Git,
            GitUrl = "https://git/x.git",
            NodeId = node,
            ProjectPath = "/srv/myapp",
            ComposePath = "/srv/myapp/compose.yml",
            ProjectUpdateMethod = ProjectUpdateMethod.Webhook,
            Branch = "main",
            WebhookSecret = "s3cr3t",
            PeriodicIntervalInMinutes = 15,
            LastPeriodicUpdateAt = last,
        };

        Assert.IsAssignableFrom<BaseEntity>(p);
        Assert.NotEqual(Guid.Empty, p.Id);
        Assert.Equal("MyApp", p.ProjectName);
        Assert.Equal("myapp", p.DockerProjectName);
        Assert.Equal("desc", p.Description);
        Assert.Equal(ProjectOriginType.Git, p.ProjectOrigin);
        Assert.Equal("https://git/x.git", p.GitUrl);
        Assert.Equal(node, p.NodeId);
        Assert.Equal("/srv/myapp", p.ProjectPath);
        Assert.Equal("/srv/myapp/compose.yml", p.ComposePath);
        Assert.Equal(ProjectUpdateMethod.Webhook, p.ProjectUpdateMethod);
        Assert.Equal("main", p.Branch);
        Assert.Equal("s3cr3t", p.WebhookSecret);
        Assert.Equal(15, p.PeriodicIntervalInMinutes);
        Assert.Equal(last, p.LastPeriodicUpdateAt);
    }

    [Fact]
    public void ProjectInfo_OptionalMembers_DefaultToNull()
    {
        var p = new ProjectInfo
        {
            ProjectName = "A",
            DockerProjectName = "a",
            ProjectOrigin = ProjectOriginType.Compose,
            ProjectPath = "/a",
            ComposePath = "/a/c.yml",
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };

        Assert.Null(p.Description);
        Assert.Null(p.GitUrl);
        Assert.Null(p.NodeId);
        Assert.Null(p.Branch);
        Assert.Null(p.PeriodicIntervalInMinutes);
        // Every project gets its own random webhook secret (24 bytes, hex), never empty or shared.
        Assert.Matches("^[0-9a-f]{48}$", p.WebhookSecret);
        Assert.NotEqual(p.WebhookSecret, new ProjectInfo
        {
            ProjectName = "B", DockerProjectName = "b", ProjectOrigin = ProjectOriginType.Compose,
            ProjectPath = "/b", ComposePath = "/b/c.yml", ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        }.WebhookSecret);
        Assert.Null(p.LastPeriodicUpdateAt);
    }

    [Fact]
    public void Node_RoundTripsValues_AndDefaults()
    {
        var n = new Node
        {
            Name = "worker-1",
            TokenHash = "abc123==",
        };

        Assert.IsAssignableFrom<BaseEntity>(n);
        Assert.Equal("worker-1", n.Name);
        Assert.Equal("abc123==", n.TokenHash);
        // Runtime fields default to empty until the node registers.
        Assert.Equal("", n.MachineName);
        Assert.Equal("", n.Os);
        Assert.Equal("", n.DockerVersion);
        Assert.InRange(n.LastSeenAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        n.MachineName = "host";
        n.Os = "linux";
        n.DockerVersion = "27.0";
        Assert.Equal("host", n.MachineName);
        Assert.Equal("linux", n.Os);
        Assert.Equal("27.0", n.DockerVersion);
    }

    [Fact]
    public void Node_TokenHash_MayBeNullWhilePending()
    {
        var n = new Node { Name = "pending" };
        Assert.Null(n.TokenHash);
    }

    [Fact]
    public void ActivityEntry_RoundTripsValues_AndIsBaseEntity()
    {
        var projectId = Guid.NewGuid();
        var e = new ActivityEntry
        {
            Action = "Deploy",
            Target = "myapp",
            ProjectId = projectId,
            Details = "pulled latest",
            ActorName = "niclas",
        };

        Assert.IsAssignableFrom<BaseEntity>(e);
        Assert.Equal("Deploy", e.Action);
        Assert.Equal("myapp", e.Target);
        Assert.Equal(projectId, e.ProjectId);
        Assert.Equal("pulled latest", e.Details);
        Assert.Equal("niclas", e.ActorName);
    }

    [Fact]
    public void ActivityEntry_BackgroundJob_HasNullOptionalFields()
    {
        var e = new ActivityEntry { Action = "Update", Target = "web" };
        Assert.Null(e.ProjectId);
        Assert.Null(e.Details);
        Assert.Null(e.ActorName);
    }

    [Fact]
    public void Secret_RoundTripsValues_AndIsBaseEntity()
    {
        var cipher = new byte[] { 1, 2, 3 };
        var nonce = new byte[] { 4, 5 };
        var tag = new byte[] { 6, 7, 8, 9 };
        var s = new Secret
        {
            Name = "DB_PASSWORD",
            Ciphertext = cipher,
            Nonce = nonce,
            Tag = tag,
        };

        Assert.IsAssignableFrom<BaseEntity>(s);
        Assert.Equal("DB_PASSWORD", s.Name);
        Assert.Same(cipher, s.Ciphertext);
        Assert.Same(nonce, s.Nonce);
        Assert.Same(tag, s.Tag);
        Assert.Equal(new byte[] { 1, 2, 3 }, s.Ciphertext);
    }

    // ---------------------------------------------------------------------
    // Enums — expected member values
    // ---------------------------------------------------------------------

    [Fact]
    public void ProjectOriginType_HasExpectedValues()
    {
        Assert.Equal(0, (int)ProjectOriginType.Unknown);
        Assert.Equal(1, (int)ProjectOriginType.Git);
        Assert.Equal(2, (int)ProjectOriginType.Compose);
        Assert.Equal(3, (int)ProjectOriginType.Import);
        Assert.Equal(4, Enum.GetValues<ProjectOriginType>().Length);
    }

    [Fact]
    public void ProjectUpdateMethod_HasExpectedValues()
    {
        Assert.Equal(0, (int)ProjectUpdateMethod.Unknown);
        Assert.Equal(1, (int)ProjectUpdateMethod.Webhook);
        Assert.Equal(2, (int)ProjectUpdateMethod.Manual);
        Assert.Equal(3, (int)ProjectUpdateMethod.Periodically);
        Assert.Equal(4, Enum.GetValues<ProjectUpdateMethod>().Length);
    }

    [Fact]
    public void UpdateMethodType_HasExpectedValues()
    {
        Assert.Equal(0, (int)UpdateMethodType.Unknown);
        Assert.Equal(1, (int)UpdateMethodType.Created);
        Assert.Equal(2, (int)UpdateMethodType.Stopped);
        Assert.Equal(3, (int)UpdateMethodType.Running);
        Assert.Equal(4, (int)UpdateMethodType.Updating);
        Assert.Equal(5, (int)UpdateMethodType.Crashed);
        Assert.Equal(6, Enum.GetValues<UpdateMethodType>().Length);
    }

    // ---------------------------------------------------------------------
    // Application models
    // ---------------------------------------------------------------------

    [Fact]
    public void SystemPaths_RoundTrips()
    {
        var sp = new SystemPaths { ProjectsPath = "/var/projects", DockerSocket = "unix:///var/run/docker.sock" };
        Assert.Equal("/var/projects", sp.ProjectsPath);
        Assert.Equal("unix:///var/run/docker.sock", sp.DockerSocket);

        var noSocket = new SystemPaths { ProjectsPath = "/p" };
        Assert.Null(noSocket.DockerSocket);
    }

    [Fact]
    public void DockiUpProjectConfig_Defaults_AreUnknownAndMinValue()
    {
        var cfg = new DockiUpProjectConfig();
        Assert.Equal(string.Empty, cfg.Name);
        Assert.Equal(ProjectOriginType.Unknown, cfg.Origin);
        Assert.Equal(UpdateMethodType.Unknown, cfg.UpdateMethod);
        Assert.Equal(DateTime.MinValue, cfg.LastUpdated);
    }

    [Fact]
    public void DockiUpProjectConfig_RoundTrips()
    {
        var when = new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc);
        var cfg = new DockiUpProjectConfig
        {
            Name = "myapp",
            Origin = ProjectOriginType.Git,
            UpdateMethod = UpdateMethodType.Running,
            LastUpdated = when,
        };
        Assert.Equal("myapp", cfg.Name);
        Assert.Equal(ProjectOriginType.Git, cfg.Origin);
        Assert.Equal(UpdateMethodType.Running, cfg.UpdateMethod);
        Assert.Equal(when, cfg.LastUpdated);
    }

    // ---------------------------------------------------------------------
    // DTOs
    // ---------------------------------------------------------------------

    [Fact]
    public void ActivityEntryDto_RoundTrips()
    {
        var id = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var created = DateTime.UtcNow;
        var dto = new ActivityEntryDto
        {
            Id = id,
            Action = "Start",
            Target = "web",
            ProjectId = projectId,
            Details = "ok",
            ActorName = "niclas",
            CreatedAt = created,
        };

        Assert.Equal(id, dto.Id);
        Assert.Equal("Start", dto.Action);
        Assert.Equal("web", dto.Target);
        Assert.Equal(projectId, dto.ProjectId);
        Assert.Equal("ok", dto.Details);
        Assert.Equal("niclas", dto.ActorName);
        Assert.Equal(created, dto.CreatedAt);
    }

    [Fact]
    public void SecretDto_RoundTrips()
    {
        var id = Guid.NewGuid();
        var created = DateTime.UtcNow;
        var dto = new SecretDto { Id = id, Name = "API_KEY", CreatedAt = created };
        Assert.Equal(id, dto.Id);
        Assert.Equal("API_KEY", dto.Name);
        Assert.Equal(created, dto.CreatedAt);
    }

    [Fact]
    public void AppInfoDto_RoundTrips()
    {
        var dto = new AppInfoDto { Version = "1.2.3", Environment = "Production" };
        Assert.Equal("1.2.3", dto.Version);
        Assert.Equal("Production", dto.Environment);
    }

    [Fact]
    public void ContainerDto_RoundTrips()
    {
        var dto = new ContainerDto
        {
            Id = "abc",
            Name = "myapp-web-1",
            Status = "Up 3 minutes",
            State = UpdateMethodType.Running,
            ServiceName = "web",
            ProjectName = "myapp",
        };
        Assert.Equal("abc", dto.Id);
        Assert.Equal("myapp-web-1", dto.Name);
        Assert.Equal("Up 3 minutes", dto.Status);
        Assert.Equal(UpdateMethodType.Running, dto.State);
        Assert.Equal("web", dto.ServiceName);
        Assert.Equal("myapp", dto.ProjectName);
    }

    [Fact]
    public void DashboardStatsDto_RoundTrips_WithActivityList()
    {
        var activity = new ActivityEntryDto
        {
            Id = Guid.NewGuid(),
            Action = "Deploy",
            Target = "web",
            CreatedAt = DateTime.UtcNow,
        };
        var dto = new DashboardStatsDto
        {
            TotalProjects = 5,
            TotalContainers = 12,
            RunningContainers = 9,
            RecentActivity = new List<ActivityEntryDto> { activity },
        };

        Assert.Equal(5, dto.TotalProjects);
        Assert.Equal(12, dto.TotalContainers);
        Assert.Equal(9, dto.RunningContainers);
        Assert.Single(dto.RecentActivity);
        Assert.Same(activity, dto.RecentActivity[0]);
    }

    [Fact]
    public void ProjectDto_RoundTrips()
    {
        var id = Guid.NewGuid();
        var node = Guid.NewGuid();
        var container = new ContainerDto
        {
            Id = "c1",
            Name = "myapp-web-1",
            Status = "Up",
            State = UpdateMethodType.Running,
            ServiceName = "web",
            ProjectName = "myapp",
        };
        var dto = new ProjectDto
        {
            Id = id,
            ProjectName = "MyApp",
            DockerProjectName = "myapp",
            ProjectDescription = "desc",
            ManagedByDockiUp = true,
            NodeId = node,
            Containers = new[] { container },
            ProjectPath = "/srv/myapp",
            UpdateMethod = "Webhook",
        };

        Assert.Equal(id, dto.Id);
        Assert.Equal("MyApp", dto.ProjectName);
        Assert.Equal("myapp", dto.DockerProjectName);
        Assert.Equal("desc", dto.ProjectDescription);
        Assert.True(dto.ManagedByDockiUp);
        Assert.Equal(node, dto.NodeId);
        Assert.Single(dto.Containers);
        Assert.Same(container, dto.Containers[0]);
        Assert.Equal("/srv/myapp", dto.ProjectPath);
        Assert.Equal("Webhook", dto.UpdateMethod);
    }

    [Fact]
    public void ProjectDto_UnmanagedDefaults_AllowNullableFields()
    {
        var dto = new ProjectDto
        {
            ProjectName = "Ext",
            DockerProjectName = "ext",
            ProjectDescription = "unmanaged",
            ManagedByDockiUp = false,
            Containers = Array.Empty<ContainerDto>(),
        };
        Assert.Null(dto.Id);
        Assert.Null(dto.NodeId);
        Assert.Null(dto.ProjectPath);
        Assert.Null(dto.UpdateMethod);
        Assert.Empty(dto.Containers);
    }

    [Fact]
    public void SetupProjectDto_RoundTrips()
    {
        var node = Guid.NewGuid();
        var dto = new SetupProjectDto
        {
            ProjectName = "MyApp",
            Description = "desc",
            ProjectOrigin = ProjectOriginType.Git,
            GitUrl = "https://git/x.git",
            Compose = "services: {}",
            Path = "/srv/myapp",
            NodeId = node,
            ProjectUpdateMethod = ProjectUpdateMethod.Periodically,
            Branch = "main",
            ComposeFile = "deploy/compose.yml",
            PeriodicIntervalInMinutes = 30,
        };

        Assert.Equal("MyApp", dto.ProjectName);
        Assert.Equal("desc", dto.Description);
        Assert.Equal(ProjectOriginType.Git, dto.ProjectOrigin);
        Assert.Equal("https://git/x.git", dto.GitUrl);
        Assert.Equal("services: {}", dto.Compose);
        Assert.Equal("/srv/myapp", dto.Path);
        Assert.Equal(node, dto.NodeId);
        Assert.Equal(ProjectUpdateMethod.Periodically, dto.ProjectUpdateMethod);
        Assert.Equal("main", dto.Branch);
        Assert.Equal("deploy/compose.yml", dto.ComposeFile);
        Assert.Equal(30, dto.PeriodicIntervalInMinutes);
    }

    [Fact]
    public void SetupProjectDto_OptionalMembers_DefaultToNull()
    {
        var dto = new SetupProjectDto
        {
            ProjectName = "Min",
            ProjectOrigin = ProjectOriginType.Compose,
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };
        Assert.Null(dto.Description);
        Assert.Null(dto.GitUrl);
        Assert.Null(dto.Compose);
        Assert.Null(dto.Path);
        Assert.Null(dto.NodeId);
        Assert.Null(dto.Branch);
        Assert.Null(dto.ComposeFile);
        Assert.Null(dto.PeriodicIntervalInMinutes);
    }
}
