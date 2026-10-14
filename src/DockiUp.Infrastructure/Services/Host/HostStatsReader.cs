using System.Diagnostics;
using System.Text.RegularExpressions;
using DockiUp.Application.Dtos;

namespace DockiUp.Infrastructure.Services.Host
{
    /// <summary>Reads the stats of the machine DockiUp (or a node) runs on. With the host's root mounted read-only at
    /// /host it sees everything (disks, real network, other processes); without it CPU, memory and load still work,
    /// because Linux doesn't namespace those. One per process: CPU % and rates are deltas against the previous read.</summary>
    public sealed partial class HostStatsReader
    {
        public static HostStatsReader Shared { get; } = new(Directory.Exists("/host/proc/1") ? "/host" : null);

        private readonly string? _root;
        private readonly string _proc;
        private readonly string _sys;
        private readonly Lock _gate = new();
        private (long Ticks, long Busy, long Total, long Rx, long Tx, long Read, long Written)? _previous;
        private readonly Lazy<string?> _nvidiaSmi = new(() => FindOnPath("nvidia-smi"));

        public HostStatsReader(string? root)
        {
            _root = root;
            _proc = root is null ? "/proc" : $"{root}/proc";
            _sys = root is not null && Directory.Exists($"{root}/sys/class") ? $"{root}/sys" : "/sys";
        }

        public bool HostView => _root is not null;

        public async Task<HostStatsDto> ReadAsync(CancellationToken cancellationToken = default)
        {
            // Not Linux (a dev machine): no stats rather than zeros that look real.
            if (!File.Exists($"{_proc}/stat")) throw new PlatformNotSupportedException("Host stats need Linux.");

            // The very first read has nothing to compare with: take a second one shortly after.
            if (_previous is null) { Counters(); await Task.Delay(500, cancellationToken); }
            var now = Counters();
            var (cpuBusy, cpuTotal, rx, tx, read, written) = (now.Busy, now.Total, now.Rx, now.Tx, now.Read, now.Written);
            double? Rate(long current, long? before, double seconds) => before is { } b && current >= b && seconds > 0 ? (current - b) / seconds : null;

            (long Ticks, long Busy, long Total, long Rx, long Tx, long Read, long Written) prev;
            lock (_gate) { prev = _previous!.Value; _previous = now; }
            var seconds = TimeSpan.FromTicks(now.Ticks - prev.Ticks).TotalSeconds;

            var memory = HostParsers.Memory(Read("meminfo"));
            var load = HostParsers.Load(Read("loadavg"));
            var cores = HostParsers.Cpu(Read("stat")).Cores;
            var iface = HostView ? HostParsers.DefaultInterface(Read("1/net/route")) : null;

            return new HostStatsDto
            {
                Timestamp = DateTime.UtcNow,
                UptimeSeconds = HostParsers.Uptime(Read("uptime")),
                Cores = cores,
                CpuPercent = cpuTotal > prev.Total ? Math.Round(100.0 * (cpuBusy - prev.Busy) / (cpuTotal - prev.Total), 1) : 0,
                Load1 = load.One, Load5 = load.Five, Load15 = load.Fifteen,
                MemoryTotal = memory.Total, MemoryUsed = memory.Used, MemoryCache = memory.Cache,
                SwapTotal = memory.SwapTotal, SwapUsed = memory.SwapUsed,
                Disks = Disks(),
                // Without the host view /proc/net/dev is the container's own network, not the machine's.
                NetworkRxRate = HostView ? Rate(rx, prev.Rx, seconds) : null,
                NetworkTxRate = HostView ? Rate(tx, prev.Tx, seconds) : null,
                DiskReadRate = Rate(read, prev.Read, seconds),
                DiskWriteRate = Rate(written, prev.Written, seconds),
                NetworkInterface = iface,
                LinkSpeedMbps = iface is null ? null : int.TryParse(TryRead($"{_sys}/class/net/{iface}/speed"), out var speed) && speed > 0 ? speed : null,
                Gpus = await GpusAsync(cancellationToken),
                HostView = HostView,
            };
        }

        private (long Ticks, long Busy, long Total, long Rx, long Tx, long Read, long Written) Counters()
        {
            var cpu = HostParsers.Cpu(Read("stat"));
            var net = HostView ? HostParsers.Network(Read("1/net/dev")) : (0, 0);
            var io = HostParsers.DiskIo(TryRead($"{_proc}/diskstats") ?? "");
            var c = (DateTime.UtcNow.Ticks, cpu.Busy, cpu.Total, net.Item1, net.Item2, io.Read, io.Written);
            lock (_gate) _previous ??= c;
            return c;
        }

        private DiskStatsDto[] Disks()
        {
            var mounts = HostView ? HostParsers.Mounts(Read("1/mounts")) : [("", "/")];
            return mounts.Select(m =>
            {
                try
                {
                    var drive = new DriveInfo(HostView ? $"{_root}{m.Mount}" : m.Mount);
                    return drive.TotalSize > 0 ? new DiskStatsDto(m.Mount, m.Device == "" ? null : m.Device, drive.TotalSize, drive.TotalSize - drive.TotalFreeSpace) : null;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    return null; // unmounted since, or not readable
                }
            }).OfType<DiskStatsDto>().ToArray();
        }

        // ---- GPUs: the kernel's DRM view works for every vendor; nvidia-smi fills in what NVIDIA's own driver keeps to itself.

        // PCI vendor ids. Anything else still shows, named by its id.
        private static readonly Dictionary<string, string> Vendors = new()
        {
            ["0x10de"] = "NVIDIA", ["0x1002"] = "AMD", ["0x8086"] = "Intel", ["0x1ed5"] = "Moore Threads",
            ["0x1d17"] = "Zhaoxin", ["0x5143"] = "Qualcomm", ["0x1e3e"] = "Iluvatar", ["0x1d94"] = "Hygon", ["0x13b5"] = "Arm",
        };

        // Not GPUs: virtual displays and servers' management (BMC) graphics.
        private static readonly HashSet<string> NotGpus = ["0x1af4", "0x1234", "0x15ad", "0x1414", "0x1a03", "0x102b", "0x1b36"];

        [GeneratedRegex(@"^card\d+$")]
        private static partial Regex Card();

        private async Task<GpuStatsDto[]> GpusAsync(CancellationToken cancellationToken)
        {
            var gpus = new List<GpuStatsDto>();
            var drm = $"{_sys}/class/drm";
            if (Directory.Exists(drm))
            {
                foreach (var card in Directory.GetDirectories(drm).Where(d => Card().IsMatch(Path.GetFileName(d))).Order())
                {
                    var device = $"{card}/device";
                    var vendorId = TryRead($"{device}/vendor")?.Trim().ToLowerInvariant();
                    if (vendorId is null || NotGpus.Contains(vendorId)) continue;
                    var vendor = Vendors.GetValueOrDefault(vendorId) ?? $"Vendor {vendorId}";
                    var hwmon = Directory.Exists($"{device}/hwmon") ? Directory.GetDirectories($"{device}/hwmon").Order().FirstOrDefault() : null;
                    double? Hw(string file, double scale) => hwmon is not null && long.TryParse(TryRead($"{hwmon}/{file}"), out var v) ? v / scale : null;
                    var pwm = Hw("pwm1", 1);
                    var pwmMax = Hw("pwm1_max", 1) ?? 255;

                    var busId = HostParsers.NormalizeBusId(LinkName(device));
                    gpus.Add(new GpuStatsDto
                    {
                        Index = gpus.Count,
                        Name = TryRead($"{device}/product_name")?.Trim() is { Length: > 0 } product ? product
                            // NVIDIA's driver names its cards in /proc even when the container can't use them.
                            : TryRead($"{_proc}/driver/nvidia/gpus/{busId}/information") is { } info && info.Split('\n').FirstOrDefault(l => l.StartsWith("Model:")) is { } model
                                ? model["Model:".Length..].Trim()
                            : $"{vendor} GPU ({TryRead($"{device}/device")?.Trim() ?? "?"})",
                        Vendor = vendor,
                        BusId = busId,
                        Driver = LinkName($"{device}/driver"),
                        Utilization = long.TryParse(TryRead($"{device}/gpu_busy_percent"), out var busy) ? busy : null,
                        MemoryUsed = long.TryParse(TryRead($"{device}/mem_info_vram_used"), out var used) ? used : null,
                        MemoryTotal = long.TryParse(TryRead($"{device}/mem_info_vram_total"), out var total) && total > 0 ? total : null,
                        Temperature = Hw("temp1_input", 1000),
                        PowerDraw = Hw("power1_average", 1_000_000) ?? Hw("power1_input", 1_000_000),
                        PowerLimit = Hw("power1_cap", 1_000_000),
                        FanPercent = pwm is { } p ? Math.Round(100 * p / pwmMax) : null,
                    });
                }
            }

            if (_nvidiaSmi.Value is { } smi)
            {
                var nvidia = HostParsers.NvidiaSmi(await RunAsync(smi,
                    "--query-gpu=index,name,pci.bus_id,driver_version,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw,power.limit,fan.speed --format=csv,noheader,nounits",
                    cancellationToken) ?? "");
                // nvidia-smi knows more about its cards than the kernel does: it replaces their DRM entries.
                gpus.RemoveAll(g => g.Vendor == "NVIDIA" && (g.BusId is null || nvidia.Any(n => n.BusId == g.BusId)));
                gpus.AddRange(nvidia);
            }
            return [.. gpus.Select((g, i) => g with { Index = i })];
        }

        /// <summary>Processes holding GPU memory, with the Docker container each runs in. Needs the host view: without it
        /// only DockiUp's own processes are visible.</summary>
        public async Task<List<(int Pid, string Process, string? BusId, long Memory, string? ContainerId)>> GpuProcessesAsync(CancellationToken cancellationToken = default)
        {
            var found = new Dictionary<(int Pid, string? BusId), (string Process, long Memory)>();
            if (_nvidiaSmi.Value is { } smi)
                foreach (var app in HostParsers.NvidiaApps(await RunAsync(smi, "--query-compute-apps=pid,process_name,gpu_bus_id,used_memory --format=csv,noheader,nounits", cancellationToken) ?? ""))
                    found[(app.Pid, app.BusId)] = (app.Process, app.Memory);

            if (HostView)
            {
                foreach (var dir in Directory.EnumerateDirectories(_proc))
                {
                    if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
                    var clients = new Dictionary<string, (string? BusId, long Memory)>();
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles($"{dir}/fdinfo"))
                            if (TryRead(file) is { } text && text.Contains("drm-client-id") && HostParsers.DrmFdinfo(text) is { } c)
                                clients[c.ClientId] = (c.BusId, c.Memory); // several fds can share one client
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; } // exited, or not ours to read
                    foreach (var group in clients.Values.Where(c => c.Memory > 0).GroupBy(c => c.BusId))
                        if (!found.ContainsKey((pid, group.Key)))
                            found[(pid, group.Key)] = (TryRead($"{dir}/comm")?.Trim() ?? "?", group.Sum(c => c.Memory));
                }
            }

            return [.. found.Select(f => (f.Key.Pid, f.Value.Process, f.Key.BusId, f.Value.Memory,
                    HostView ? HostParsers.ContainerId(TryRead($"{_proc}/{f.Key.Pid}/cgroup") ?? "") : null))
                .OrderByDescending(p => p.Memory)];
        }

        private string Read(string relative) => File.ReadAllText($"{_proc}/{relative}");

        private static string? TryRead(string path)
        {
            try { return File.ReadAllText(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }

        private static string? LinkName(string path)
        {
            try { return new FileInfo(path).ResolveLinkTarget(true)?.Name ?? new DirectoryInfo(path).ResolveLinkTarget(true)?.Name; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }

        private static string? FindOnPath(string name)
            => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists);

        private static async Task<string?> RunAsync(string file, string arguments, CancellationToken cancellationToken)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true });
                if (process is null) return null;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                return process.ExitCode == 0 ? output : null;
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return null; // no driver loaded, or it hung
            }
        }
    }
}
