using System.Globalization;
using System.Text.RegularExpressions;

namespace DockiUp.Infrastructure.Services.Host
{
    /// <summary>Pure parsers for the Linux /proc and /sys files host stats come from, kept apart from file access so
    /// they can be tested with real samples.</summary>
    public static partial class HostParsers
    {
        /// <summary>/proc/stat: busy and total jiffies over all CPUs, and the CPU count.</summary>
        public static (long Busy, long Total, int Cores) Cpu(string stat)
        {
            long busy = 0, total = 0;
            var cores = 0;
            foreach (var line in stat.Split('\n'))
            {
                if (line.StartsWith("cpu ", StringComparison.Ordinal))
                {
                    var v = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
                    // user nice system idle iowait irq softirq steal (guest time is already inside user/nice)
                    total = v.Take(8).Sum();
                    busy = total - v[3] - (v.Length > 4 ? v[4] : 0);
                }
                else if (CpuLine().IsMatch(line)) cores++;
            }
            return (busy, total, cores);
        }

        [GeneratedRegex(@"^cpu\d+ ")]
        private static partial Regex CpuLine();

        /// <summary>/proc/meminfo in bytes: total, used (total minus available), cache, swap total and used.</summary>
        public static (long Total, long Used, long Cache, long SwapTotal, long SwapUsed) Memory(string meminfo)
        {
            var kb = meminfo.Split('\n')
                .Select(l => l.Split(':', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => long.TryParse(p[1].Trim().Split(' ')[0], out var n) ? n * 1024 : 0);
            long Get(string k) => kb.GetValueOrDefault(k);
            var total = Get("MemTotal");
            var available = kb.ContainsKey("MemAvailable") ? Get("MemAvailable") : Get("MemFree") + Get("Buffers") + Get("Cached");
            var used = Math.Max(0, total - available);
            var cache = Math.Clamp(Get("Buffers") + Get("Cached") + Get("SReclaimable") - Get("Shmem"), 0, total - used);
            return (total, used, cache, Get("SwapTotal"), Math.Max(0, Get("SwapTotal") - Get("SwapFree")));
        }

        public static (double One, double Five, double Fifteen) Load(string loadavg)
        {
            var p = loadavg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (D(p[0]), D(p[1]), D(p[2]));
        }

        public static long Uptime(string uptime) => (long)D(uptime.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);

        // Bridges, veths and VPN/overlay interfaces carry traffic that also passes a real interface (or never leaves
        // the host): counting them would double it.
        [GeneratedRegex(@"^(lo|veth|docker|br-|virbr|vnet|cni|flannel|cali|vxlan|weave|kube|tailscale|wg|zt|tun|tap)")]
        private static partial Regex VirtualInterface();

        /// <summary>/proc/net/dev: received and sent bytes over the host's real interfaces.</summary>
        public static (long Rx, long Tx) Network(string netDev)
        {
            long rx = 0, tx = 0;
            foreach (var line in netDev.Split('\n').Skip(2))
            {
                var parts = line.Split(':', 2);
                if (parts.Length != 2 || VirtualInterface().IsMatch(parts[0].Trim())) continue;
                var v = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (v.Length < 9) continue;
                rx += long.Parse(v[0]);
                tx += long.Parse(v[8]);
            }
            return (rx, tx);
        }

        [GeneratedRegex(@"^(sd[a-z]+|vd[a-z]+|xvd[a-z]+|hd[a-z]+|nvme\d+n\d+|mmcblk\d+)$")]
        private static partial Regex WholeDisk();

        /// <summary>/proc/diskstats: bytes read and written by whole disks (partitions would count twice).</summary>
        public static (long Read, long Written) DiskIo(string diskstats)
        {
            long read = 0, written = 0;
            foreach (var line in diskstats.Split('\n'))
            {
                var v = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (v.Length < 10 || !WholeDisk().IsMatch(v[2])) continue;
                read += long.Parse(v[5]) * 512;
                written += long.Parse(v[9]) * 512;
            }
            return (read, written);
        }

        private static readonly HashSet<string> RealFilesystems = ["ext2", "ext3", "ext4", "xfs", "btrfs", "zfs", "f2fs", "jfs", "reiserfs", "bcachefs", "vfat", "exfat", "ntfs", "ntfs3", "fuseblk", "nfs", "nfs4", "cifs"];

        /// <summary>/proc/1/mounts: one entry per real filesystem (by device), the shortest mount point of each.
        /// Boot partitions and snap images are left out.</summary>
        public static List<(string Device, string Mount)> Mounts(string mounts)
        {
            return mounts.Split('\n')
                .Select(l => l.Split(' '))
                .Where(p => p.Length >= 3 && RealFilesystems.Contains(p[2]))
                .Select(p => (Device: p[0], Mount: p[1].Replace("\\040", " ")))
                .Where(m => !m.Mount.StartsWith("/boot", StringComparison.Ordinal) && !m.Mount.StartsWith("/snap", StringComparison.Ordinal)
                    && !m.Mount.StartsWith("/var/lib/docker", StringComparison.Ordinal) && !m.Mount.StartsWith("/run", StringComparison.Ordinal))
                .GroupBy(m => m.Device)
                .Select(g => g.OrderBy(m => m.Mount.Length).First())
                .OrderBy(m => m.Mount, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>/proc/net/route: the interface of the default route.</summary>
        public static string? DefaultInterface(string route)
            => route.Split('\n').Skip(1).Select(l => l.Split('\t', StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault(p => p.Length > 2 && p[1] == "00000000")?[0];

        /// <summary>`nvidia-smi --query-gpu=index,name,pci.bus_id,driver_version,utilization.gpu,memory.used,memory.total,
        /// temperature.gpu,power.draw,power.limit,fan.speed --format=csv,noheader,nounits`.</summary>
        public static List<Application.Dtos.GpuStatsDto> NvidiaSmi(string csv)
        {
            var gpus = new List<Application.Dtos.GpuStatsDto>();
            foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var v = line.Split(',').Select(s => s.Trim()).ToArray();
                if (v.Length < 11) continue;
                gpus.Add(new()
                {
                    Index = int.TryParse(v[0], out var i) ? i : gpus.Count,
                    Name = v[1],
                    Vendor = "NVIDIA",
                    BusId = NormalizeBusId(v[2]),
                    Driver = Nullable(v[3]),
                    Utilization = Number(v[4]),
                    MemoryUsed = Number(v[5]) is { } used ? (long)(used * 1024 * 1024) : null,
                    MemoryTotal = Number(v[6]) is { } total ? (long)(total * 1024 * 1024) : null,
                    Temperature = Number(v[7]),
                    PowerDraw = Number(v[8]),
                    PowerLimit = Number(v[9]),
                    FanPercent = Number(v[10]),
                });
            }
            return gpus;
        }

        /// <summary>`nvidia-smi --query-compute-apps=pid,process_name,gpu_bus_id,used_memory --format=csv,noheader,nounits`.</summary>
        public static List<(int Pid, string Process, string? BusId, long Memory)> NvidiaApps(string csv)
            => csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split(',').Select(s => s.Trim()).ToArray())
                .Where(v => v.Length >= 4 && int.TryParse(v[0], out _))
                .Select(v => (int.Parse(v[0]), Path.GetFileName(v[1]), NormalizeBusId(v[2]), (long)((Number(v[3]) ?? 0) * 1024 * 1024)))
                .ToList();

        /// <summary>A DRM client's fdinfo (any vendor's open-source driver): its id, the GPU's PCI address and the
        /// memory it holds, VRAM if the driver reports it, else its resident memory. Null when the fd isn't a GPU.</summary>
        public static (string ClientId, string? BusId, long Memory)? DrmFdinfo(string fdinfo)
        {
            string? client = null, pdev = null;
            long vram = 0, resident = 0;
            foreach (var line in fdinfo.Split('\n'))
            {
                var p = line.Split(':', 2);
                if (p.Length != 2) continue;
                var key = p[0].Trim();
                var value = p[1].Trim();
                if (key == "drm-client-id") client = value;
                else if (key == "drm-pdev") pdev = value;
                else if (key.StartsWith("drm-", StringComparison.Ordinal) && key.Contains("vram")) vram = Math.Max(vram, Size(value));
                else if (key.StartsWith("drm-resident-", StringComparison.Ordinal)) resident += Size(value);
            }
            return client is null ? null : (client, NormalizeBusId(pdev), vram > 0 ? vram : resident);
        }

        [GeneratedRegex(@"[0-9a-f]{64}")]
        private static partial Regex ContainerIdPattern();

        /// <summary>/proc/&lt;pid&gt;/cgroup: the Docker container the process runs in, if any.</summary>
        public static string? ContainerId(string cgroup) => ContainerIdPattern().Match(cgroup) is { Success: true } m ? m.Value : null;

        /// <summary>PCI addresses as 0000:01:00.0 (nvidia-smi writes 00000000:01:00.0).</summary>
        public static string? NormalizeBusId(string? busId)
        {
            if (string.IsNullOrWhiteSpace(busId)) return null;
            var b = busId.Trim().ToLowerInvariant();
            var colon = b.IndexOf(':');
            return colon > 4 ? b[(colon - 4)..] : b;
        }

        private static long Size(string value)
        {
            var p = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0 || !long.TryParse(p[0], out var n)) return 0;
            return n * (p.Length > 1 ? p[1].ToUpperInvariant() switch { "KIB" => 1024L, "MIB" => 1024L * 1024, "GIB" => 1024L * 1024 * 1024, _ => 1 } : 1);
        }

        private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        private static string? Nullable(string s) => s is "" or "[N/A]" or "N/A" or "[Not Supported]" ? null : s;

        private static double? Number(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
