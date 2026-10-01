using Docker.DotNet;
using Docker.DotNet.Models;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.TestSupport;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Infrastructure;

/// <summary>Stats math, log parameters, resource listing and prune filters of the local DockerService.</summary>
public class MonitoringDockerTests
{
    private sealed class Daemon
    {
        public readonly Mock<IContainerOperations> Containers = new();
        public readonly Mock<IImageOperations> Images = new();
        public readonly Mock<IVolumeOperations> Volumes = new();
        public readonly Mock<INetworkOperations> Networks = new();
        public readonly DockerService Service;

        public Daemon()
        {
            var docker = new Mock<IDockerClient>();
            docker.Setup(d => d.Containers).Returns(Containers.Object);
            docker.Setup(d => d.Images).Returns(Images.Object);
            docker.Setup(d => d.Volumes).Returns(Volumes.Object);
            docker.Setup(d => d.Networks).Returns(Networks.Object);
            var client = new Mock<IDockiUpDockerClient>();
            client.Setup(c => c.DockerClient).Returns(docker.Object);
            // An unreachable DOCKER_HOST makes the best-effort `docker system df` fail fast instead of reading this machine.
            Service = new DockerService(client.Object, TestDb.Create(),
                Options.Create(new SystemPaths { ProjectsPath = "/p", DockerSocket = "tcp://127.0.0.1:1" }),
                new Mock<IDockiUpProjectConfigurationService>().Object);
        }
    }

    private static CPUStats Cpu(ulong total, ulong system, uint online = 0, int perCpu = 0) => new()
    {
        CPUUsage = new CPUUsage { TotalUsage = total, PercpuUsage = Enumerable.Repeat(0UL, perCpu).ToList() },
        SystemUsage = system,
        OnlineCPUs = online,
    };

    // ---- CPU / memory ----

    [Fact]
    public void CpuPercent_IsCpuDeltaOverSystemDelta_TimesCores()
    {
        // 200 of 1000 system ticks on a 4-core host = 20% of the host = 80% of one core.
        Assert.Equal(80, DockerStatsMath.CpuPercent(Cpu(1200, 11000, online: 4), Cpu(1000, 10000, online: 4)));
    }

    [Fact]
    public void CpuPercent_FallsBackToPerCpuCount_ThenOneCore()
    {
        Assert.Equal(40, DockerStatsMath.CpuPercent(Cpu(1200, 11000, perCpu: 2), Cpu(1000, 10000)));
        Assert.Equal(20, DockerStatsMath.CpuPercent(Cpu(1200, 11000), Cpu(1000, 10000)));
    }

    [Fact]
    public void CpuPercent_IsZero_WithoutAPreviousReadingOrProgress()
    {
        Assert.Equal(0, DockerStatsMath.CpuPercent(Cpu(1200, 11000, 4), new CPUStats()));
        Assert.Equal(0, DockerStatsMath.CpuPercent(Cpu(1000, 10000, 4), Cpu(1000, 10000, 4)));
        Assert.Equal(0, DockerStatsMath.CpuPercent(Cpu(1000, 10000, 4), null));
    }

    [Fact]
    public void MemoryUsed_SubtractsPageCache_CgroupV2AndV1()
    {
        Assert.Equal(700, DockerStatsMath.MemoryUsed(new MemoryStats { Usage = 1000, Stats = new Dictionary<string, ulong> { ["inactive_file"] = 300 } }));
        Assert.Equal(900, DockerStatsMath.MemoryUsed(new MemoryStats { Usage = 1000, Stats = new Dictionary<string, ulong> { ["total_inactive_file"] = 100 } }));
        Assert.Equal(1000, DockerStatsMath.MemoryUsed(new MemoryStats { Usage = 1000, Stats = new Dictionary<string, ulong> { ["inactive_file"] = 5000 } }));
        Assert.Equal(1000, DockerStatsMath.MemoryUsed(new MemoryStats { Usage = 1000 }));
        Assert.Equal(0, DockerStatsMath.MemoryUsed(null));
    }

    [Fact]
    public void ToDto_SumsNetworksAndMapsMemory()
    {
        var stats = new ContainerStatsResponse
        {
            CPUStats = Cpu(1200, 11000, 1),
            PreCPUStats = Cpu(1000, 10000, 1),
            MemoryStats = new MemoryStats { Usage = 500, Limit = 2000 },
            Networks = new Dictionary<string, NetworkStats>
            {
                ["eth0"] = new() { RxBytes = 10, TxBytes = 20 },
                ["eth1"] = new() { RxBytes = 1, TxBytes = 2 },
            },
        };
        var now = DateTime.UtcNow;
        var dto = DockerStatsMath.ToDto("cid", "web", "proj", stats, now);
        Assert.Equal(new ContainerStatsDto("cid", "web", "proj", 20, 500, 2000, 11, 22, now), dto);
    }

    // ---- CLI size parsing ----

    [Theory]
    [InlineData("0B", 0L)]
    [InlineData("45.1kB", 45100L)]
    [InlineData("1.068MB", 1068000L)]
    [InlineData("8.085GB", 8085000000L)]
    [InlineData("133.9GB (86%)", 133900000000L)]
    [InlineData("N/A", null)]
    [InlineData("", null)]
    [InlineData("12XB", null)]
    public void ParseSize_ReadsDockerHumanSizes(string text, long? expected)
        => Assert.Equal(expected, DockerStatsMath.ParseSize(text));

    [Fact]
    public void ParseDiskUsage_MapsEachType()
    {
        var json = """
            {"Active":"20","Reclaimable":"1GB (86%)","Size":"2GB","TotalCount":"155","Type":"Images"}
            {"Active":"15","Reclaimable":"13MB (0%)","Size":"8GB","TotalCount":"27","Type":"Containers"}
            {"Active":"13","Reclaimable":"3GB (67%)","Size":"4GB","TotalCount":"77","Type":"Local Volumes"}
            {"Active":"0","Reclaimable":"17GB","Size":"70GB","TotalCount":"857","Type":"Build Cache"}
            """;
        Assert.Equal(new DiskUsageDto(2_000_000_000, 1_000_000_000, 8_000_000_000, 4_000_000_000, 3_000_000_000, 70_000_000_000),
            DockerStatsMath.ParseDiskUsage(json));
    }

    [Fact]
    public void ParseVolumeSizes_ReadsVerboseDf()
    {
        var sizes = DockerStatsMath.ParseVolumeSizes("""{"Images":[],"Volumes":[{"Name":"data","Size":"1.5MB"},{"Name":"x","Size":"N/A"}]}""");
        Assert.Equal(1_500_000, Assert.Single(sizes).Value);
        Assert.Empty(DockerStatsMath.ParseVolumeSizes(""));
    }

    // ---- Logs (#72) ----

    [Theory]
    [InlineData(null, "100")]
    [InlineData(500, "500")]
    [InlineData(0, "all")]
    [InlineData(-1, "all")]
    public void BuildLogParameters_MapsTail(int? tail, string expected)
        => Assert.Equal(expected, DockerService.BuildLogParameters(new ContainerLogOptions(tail)).Tail);

    [Fact]
    public void BuildLogParameters_PassesStreamsAndTimestamps()
    {
        var p = DockerService.BuildLogParameters(new ContainerLogOptions(10, Stdout: false, Stderr: true, Timestamps: true));
        Assert.False(p.ShowStdout);
        Assert.True(p.ShowStderr);
        Assert.True(p.Timestamps);
    }

    [Fact]
    public async Task GetContainerLogs_WithOptions_SendsThemToTheDaemon()
    {
        var daemon = new Daemon();
#pragma warning disable CS0618
        daemon.Containers.Setup(c => c.GetContainerLogsAsync("cid", It.IsAny<ContainerLogsParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());
        await daemon.Service.GetContainerLogsAsync("cid", new ContainerLogOptions(0, Stdout: true, Stderr: false, Timestamps: true));
        daemon.Containers.Verify(c => c.GetContainerLogsAsync("cid",
            It.Is<ContainerLogsParameters>(p => p.ShowStdout == true && p.ShowStderr == false && p.Timestamps == true && p.Tail == "all"),
            It.IsAny<CancellationToken>()), Times.Once);
#pragma warning restore CS0618
    }

    [Fact]
    public async Task GetContainerLogs_NeitherStream_IsEmpty_WithoutCallingTheDaemon()
    {
        var daemon = new Daemon();
        Assert.Equal("", await daemon.Service.GetContainerLogsAsync("cid", new ContainerLogOptions(100, false, false)));
        Assert.Empty(daemon.Containers.Invocations);
    }

    // ---- Stats sampling ----

    [Fact]
    public async Task GetContainerStats_SamplesEveryRunningContainer_SkipsOnesThatFail()
    {
        var daemon = new Daemon();
        daemon.Containers.Setup(c => c.ListContainersAsync(It.Is<ContainersListParameters>(p => p.All == false), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new() { ID = "a", Names = ["/proj-web-1"], Labels = new Dictionary<string, string> { ["com.docker.compose.project"] = "proj" } },
                new() { ID = "b", Names = ["/gone"], Labels = new Dictionary<string, string>() },
            });
        daemon.Containers.Setup(c => c.GetContainerStatsAsync("a", It.Is<ContainerStatsParameters>(p => !p.Stream), It.IsAny<IProgress<ContainerStatsResponse>>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerStatsParameters, IProgress<ContainerStatsResponse>, CancellationToken>((_, _, progress, _) =>
                progress.Report(new ContainerStatsResponse { CPUStats = Cpu(1200, 11000, 2), PreCPUStats = Cpu(1000, 10000, 2), MemoryStats = new MemoryStats { Usage = 42, Limit = 100 } }))
            .Returns(Task.CompletedTask);
        daemon.Containers.Setup(c => c.GetContainerStatsAsync("b", It.IsAny<ContainerStatsParameters>(), It.IsAny<IProgress<ContainerStatsResponse>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerContainerNotFoundException(System.Net.HttpStatusCode.NotFound, "gone"));

        var stats = await daemon.Service.GetContainerStatsAsync();

        var s = Assert.Single(stats);
        Assert.Equal(("a", "proj-web-1", "proj", 40d, 42L), (s.ContainerId, s.ContainerName, s.ProjectName, s.CpuPercent, s.MemoryUsage));
    }

    // ---- Resources (#73) ----

    [Fact]
    public async Task GetResources_MarksWhatContainersUse_AndBuiltinNetworks()
    {
        var daemon = new Daemon();
        daemon.Containers.Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new()
                {
                    ID = "c1", ImageID = "sha256:used",
                    Mounts = [new MountPoint { Type = "volume", Name = "data" }, new MountPoint { Type = "bind", Source = "/x" }],
                    NetworkSettings = new SummaryNetworkSettings { Networks = new Dictionary<string, EndpointSettings> { ["proj_default"] = new() { NetworkID = "net1" } } },
                },
            });
        daemon.Images.Setup(i => i.ListImagesAsync(It.IsAny<ImagesListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ImagesListResponse>
            {
                new() { ID = "sha256:used", RepoTags = ["web:1"], Size = 10, Created = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
                new() { ID = "sha256:dangling", RepoTags = ["<none>:<none>"], Size = 5, Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            });
        daemon.Volumes.Setup(v => v.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VolumesListResponse { Volumes = [new VolumeResponse { Name = "data", Driver = "local" }, new VolumeResponse { Name = "old", Driver = "local", CreatedAt = "2026-01-01T00:00:00Z" }] });
        daemon.Networks.Setup(n => n.ListNetworksAsync(It.IsAny<NetworksListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NetworkResponse>
            {
                new() { ID = "net1", Name = "proj_default", Driver = "bridge", Scope = "local" },
                new() { ID = "net2", Name = "bridge", Driver = "bridge", Scope = "local" },
            });

        var r = await daemon.Service.GetResourcesAsync();

        Assert.Collection(r.Images,
            i => Assert.Equal((true, false, 1, "web:1"), (i.InUse, i.Dangling, i.Containers, i.Tags.Single())),
            i => Assert.Equal((false, true), (i.InUse, i.Dangling)));
        Assert.True(r.Volumes.Single(v => v.Name == "data").InUse);
        var old = r.Volumes.Single(v => v.Name == "old");
        Assert.False(old.InUse);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), old.Created);
        Assert.Equal((true, false), (r.Networks.Single(n => n.Id == "net1").InUse, r.Networks.Single(n => n.Id == "net1").Builtin));
        Assert.Equal((false, true), (r.Networks.Single(n => n.Id == "net2").InUse, r.Networks.Single(n => n.Id == "net2").Builtin));
        Assert.Null(r.DiskUsage.ImagesSize); // CLI unreachable: summary unknown, listing still works
    }

    [Fact]
    public async Task RemoveResource_DelegatesPerKind_AndTurnsConflictIntoArgumentException()
    {
        var daemon = new Daemon();
        daemon.Images.Setup(i => i.DeleteImageAsync("img", It.IsAny<ImageDeleteParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IDictionary<string, string>>());
        await daemon.Service.RemoveResourceAsync(ResourceKind.Image, "img");
        await daemon.Service.RemoveResourceAsync(ResourceKind.Volume, "vol");
        await daemon.Service.RemoveResourceAsync(ResourceKind.Network, "net");
        daemon.Images.Verify(i => i.DeleteImageAsync("img", It.Is<ImageDeleteParameters>(p => p.Force == false), It.IsAny<CancellationToken>()));
        daemon.Volumes.Verify(v => v.RemoveAsync("vol", false, It.IsAny<CancellationToken>()));
        daemon.Networks.Verify(n => n.DeleteNetworkAsync("net", It.IsAny<CancellationToken>()));

        daemon.Volumes.Setup(v => v.RemoveAsync("busy", false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(System.Net.HttpStatusCode.Conflict, """{"message":"volume is in use"}"""));
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => daemon.Service.RemoveResourceAsync(ResourceKind.Volume, "busy"));
        Assert.Equal("volume is in use", ex.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => daemon.Service.RemoveResourceAsync(ResourceKind.Image, " "));
    }

    // ---- Prune keep-rule (#73/#74) ----

    [Fact]
    public void ContainerPruneFilters_KeepsComposeProjectContainers()
    {
        var filters = DockerService.ContainerPruneFilters(keepProjectContainers: true)!;
        Assert.True(filters["label!"]["com.docker.compose.project"]);
        Assert.Null(DockerService.ContainerPruneFilters(false));
    }

    [Fact]
    public void ImagePruneFilters_DanglingVsAllUnused()
    {
        Assert.True(DockerService.ImagePruneFilters(false)["dangling"]["true"]);
        Assert.True(DockerService.ImagePruneFilters(true)["dangling"]["false"]);
    }

    [Fact]
    public async Task Prune_RunsOnlyWhatWasAsked_AndSumsReclaimedSpace()
    {
        var daemon = new Daemon();
        daemon.Containers.Setup(c => c.PruneContainersAsync(It.IsAny<ContainersPruneParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainersPruneResponse { ContainersDeleted = ["a", "b"], SpaceReclaimed = 100 });
        daemon.Networks.Setup(n => n.PruneNetworksAsync(It.IsAny<NetworksDeleteUnusedParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetworksPruneResponse { NetworksDeleted = ["n"] });
        daemon.Images.Setup(i => i.PruneImagesAsync(It.IsAny<ImagesPruneParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImagesPruneResponse { ImagesDeleted = [new() { Untagged = "x:1" }, new() { Deleted = "sha256:x" }], SpaceReclaimed = 1000 });

        var result = await daemon.Service.PruneAsync(new PruneRequest(Containers: true, Images: true, AllImages: true, Networks: true, KeepProjectContainers: true));

        Assert.Equal(new PruneResultDto(2, 1, 0, 1, 1100), result);
        daemon.Containers.Verify(c => c.PruneContainersAsync(It.Is<ContainersPruneParameters>(p => p.Filters!.ContainsKey("label!")), It.IsAny<CancellationToken>()));
        daemon.Images.Verify(i => i.PruneImagesAsync(It.Is<ImagesPruneParameters>(p => p.Filters["dangling"].ContainsKey("false")), It.IsAny<CancellationToken>()));
        daemon.Volumes.VerifyNoOtherCalls(); // volumes only when explicitly asked
    }

    [Fact]
    public async Task Prune_Volumes_UsesAllFilter_FallsBackOnOldDaemons()
    {
        var daemon = new Daemon();
        daemon.Volumes.SetupSequence(v => v.PruneAsync(It.IsAny<VolumesPruneParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(System.Net.HttpStatusCode.BadRequest, "invalid filter 'all'"))
            .ReturnsAsync(new VolumesPruneResponse { VolumesDeleted = ["v"], SpaceReclaimed = 7 });

        var result = await daemon.Service.PruneAsync(new PruneRequest(Volumes: true));

        Assert.Equal(new PruneResultDto(0, 0, 1, 0, 7), result);
        daemon.Volumes.Verify(v => v.PruneAsync(It.Is<VolumesPruneParameters>(p => p.Filters != null && p.Filters.ContainsKey("all")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
