using DockiUp.Application.Dtos;
using DockiUp.Application.Monitoring;
using DockiUp.Domain;
using DockiUp.Infrastructure.Services.Host;
using DockiUp.Tests.TestSupport;

namespace DockiUp.Tests.Infrastructure;

/// <summary>Host stats from real-format /proc, /sys and nvidia-smi samples, and the history built from them.</summary>
public class HostStatsTests
{
    [Fact]
    public void Cpu_CountsBusyTime_AndCores()
    {
        var (busy, total, cores) = HostParsers.Cpu("""
            cpu  100 0 50 800 50 0 0 0 0 0
            cpu0 50 0 25 400 25 0 0 0 0 0
            cpu1 50 0 25 400 25 0 0 0 0 0
            intr 12345
            """);
        Assert.Equal(1000, total);
        Assert.Equal(150, busy); // idle and iowait are not busy
        Assert.Equal(2, cores);
    }

    [Fact]
    public void Memory_UsedIsTotalMinusAvailable_CacheIsReclaimable()
    {
        var m = HostParsers.Memory("""
            MemTotal:       16000000 kB
            MemFree:         2000000 kB
            MemAvailable:    9000000 kB
            Buffers:          500000 kB
            Cached:          5000000 kB
            SReclaimable:     600000 kB
            Shmem:            100000 kB
            SwapTotal:       2000000 kB
            SwapFree:        1500000 kB
            """);
        Assert.Equal(16000000L * 1024, m.Total);
        Assert.Equal(7000000L * 1024, m.Used);
        Assert.Equal(6000000L * 1024, m.Cache);
        Assert.Equal(500000L * 1024, m.SwapUsed);
    }

    [Fact]
    public void Network_CountsRealInterfacesOnly()
    {
        var (rx, tx) = HostParsers.Network("""
            Inter-|   Receive                                                |  Transmit
             face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
                lo: 5000      10    0    0    0     0          0         0     5000      10    0    0    0     0       0          0
              eth0: 1000      10    0    0    0     0          0         0     2000      10    0    0    0     0       0          0
            docker0: 9000     10    0    0    0     0          0         0     9000      10    0    0    0     0       0          0
            veth12ab: 9000    10    0    0    0     0          0         0     9000      10    0    0    0     0       0          0
              eth1: 300       10    0    0    0     0          0         0     400       10    0    0    0     0       0          0
            """);
        Assert.Equal(1300, rx);
        Assert.Equal(2400, tx);
    }

    [Fact]
    public void DiskIo_WholeDisksOnly_InBytes()
    {
        var (read, written) = HostParsers.DiskIo("""
             259       0 nvme0n1 100 0 2000 0 50 0 4000 0 0 0 0
             259       1 nvme0n1p1 100 0 2000 0 50 0 4000 0 0 0 0
               8       0 sda 10 0 100 0 5 0 200 0 0 0 0
               7       0 loop0 10 0 999 0 5 0 999 0 0 0 0
            """);
        Assert.Equal(2100L * 512, read);
        Assert.Equal(4200L * 512, written);
    }

    [Fact]
    public void Mounts_RealFilesystems_OncePerDevice()
    {
        var mounts = HostParsers.Mounts("""
            /dev/sda1 / ext4 rw 0 0
            /dev/sda1 /var/lib/docker/overlay2 ext4 rw 0 0
            /dev/sda15 /boot/efi vfat rw 0 0
            /dev/sdb1 /mnt/models xfs rw 0 0
            /dev/sdb1 /srv/models xfs rw 0 0
            overlay /var/lib/docker/overlay2/x/merged overlay rw 0 0
            tmpfs /run tmpfs rw 0 0
            proc /proc proc rw 0 0
            """);
        Assert.Equal([("/dev/sda1", "/"), ("/dev/sdb1", "/mnt/models")], mounts);
    }

    [Fact]
    public void DefaultInterface_FromTheRouteTable()
        => Assert.Equal("eth0", HostParsers.DefaultInterface(
            "Iface\tDestination\tGateway\tFlags\nbr-1\t000011AC\t00000000\t0001\neth0\t00000000\t0101A8C0\t0003\n"));

    [Fact]
    public void NvidiaSmi_ParsesCards_AndLeavesUnsupportedOut()
    {
        var gpu = Assert.Single(HostParsers.NvidiaSmi("0, NVIDIA GeForce RTX 3090, 00000000:01:00.0, 550.54, 71, 19149, 24576, 66, 284.12, 350.00, [N/A]\n"));
        Assert.Equal("NVIDIA GeForce RTX 3090", gpu.Name);
        Assert.Equal("0000:01:00.0", gpu.BusId);
        Assert.Equal(71, gpu.Utilization);
        Assert.Equal(24576L * 1024 * 1024, gpu.MemoryTotal);
        Assert.Null(gpu.FanPercent); // [N/A], not 0
    }

    [Fact]
    public void DrmFdinfo_TakesVram_AndTheClient()
    {
        var c = HostParsers.DrmFdinfo("""
            pos:	0
            drm-driver:	amdgpu
            drm-client-id:	42
            drm-pdev:	0000:03:00.0
            drm-memory-vram:	524288 KiB
            drm-memory-gtt:	1024 KiB
            """);
        Assert.Equal(("42", "0000:03:00.0", 524288L * 1024), c);
        Assert.Null(HostParsers.DrmFdinfo("pos:\t0\nflags:\t02\n")); // not a GPU fd
    }

    [Fact]
    public void ContainerId_FromCgroup()
        => Assert.Equal(new string('a', 64), HostParsers.ContainerId($"0::/system.slice/docker-{new string('a', 64)}.scope\n"));

    [Fact]
    public void Store_EmitsAMinute_WhenTheNextOneStarts()
    {
        var store = new HostStatsStore();
        var minute = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        HostStatsDto At(int second, double cpu, double? gpu) => new()
        {
            Timestamp = minute.AddSeconds(second), CpuPercent = cpu, MemoryTotal = 100, MemoryUsed = 40,
            Gpus = gpu is null ? [] : [new GpuStatsDto { Name = "a", Vendor = "AMD", Utilization = gpu, MemoryUsed = 2, MemoryTotal = 8 }, new GpuStatsDto { Name = "b", Vendor = "Intel" }],
        };

        Assert.Null(store.Ingest(null, At(0, 10, 50)));
        Assert.Null(store.Ingest(null, At(30, 30, 70)));
        var done = store.Ingest(null, At(60, 99, 0));

        Assert.NotNull(done);
        Assert.Equal(minute, done.Timestamp);
        Assert.Equal(20, done.CpuPercent);
        Assert.Equal(60, done.GpuUtilization); // over the GPUs that report a load
        Assert.Equal(8, done.GpuMemoryTotal);
        Assert.Equal(99, store.Get(null)!.CpuPercent);
    }

    [Fact]
    public async Task History_AWeek_IsThinnedToAbout360Points()
    {
        var db = TestDb.Create();
        var now = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc);
        for (var m = 0; m < 7 * 24 * 60; m += 5)
            db.HostStatSamples.Add(new HostStatSample { Timestamp = now.AddMinutes(-m), CpuPercent = 50, MemoryTotal = 1 });
        db.HostStatSamples.Add(new HostStatSample { NodeId = Guid.NewGuid(), Timestamp = now, CpuPercent = 1 }); // another host
        await db.SaveChangesAsync();

        var week = await HostStatsHistory.QueryAsync(db, null, 168, now);
        var hour = await HostStatsHistory.QueryAsync(db, null, 1, now);

        Assert.InRange(week.Length, 300, 361);
        Assert.All(week, p => Assert.Equal(50, p.CpuPercent));
        Assert.Equal(13, hour.Length); // one every 5 minutes, both ends included
    }
}
