using System.Globalization;
using System.Text.Json;
using Docker.DotNet.Models;
using DockiUp.Application.Dtos;

namespace DockiUp.Infrastructure.Services
{
    /// <summary>Pure helpers that turn raw daemon output into the numbers DockiUp shows (same formulas as the docker CLI).</summary>
    public static class DockerStatsMath
    {
        /// <summary>CPU % over the window between precpu and cpu (one-shot stats with Stream=false carry both).
        /// 100% = one full core, so a busy 4-core container can show up to 400%, like `docker stats`.</summary>
        public static double CpuPercent(CPUStats? cpu, CPUStats? precpu)
        {
            if (cpu?.CPUUsage is null || precpu?.CPUUsage is null)
                return 0;
            var cpuDelta = (double)cpu.CPUUsage.TotalUsage - precpu.CPUUsage.TotalUsage;
            var systemDelta = (double)cpu.SystemUsage - precpu.SystemUsage;
            if (cpuDelta <= 0 || systemDelta <= 0)
                return 0;
            double cpus = cpu.OnlineCPUs > 0 ? cpu.OnlineCPUs : cpu.CPUUsage.PercpuUsage?.Count ?? 0;
            if (cpus <= 0) cpus = 1;
            return Math.Round(cpuDelta / systemDelta * cpus * 100, 2);
        }

        /// <summary>Usage minus page cache (cgroup v2 "inactive_file", v1 "total_inactive_file"), as the docker CLI shows it.</summary>
        public static long MemoryUsed(MemoryStats? memory)
        {
            if (memory is null) return 0;
            ulong cache = 0;
            if (memory.Stats is { } stats
                && (stats.TryGetValue("inactive_file", out var v2) || stats.TryGetValue("total_inactive_file", out v2)))
                cache = v2;
            return (long)(cache < memory.Usage ? memory.Usage - cache : memory.Usage);
        }

        public static ContainerStatsDto ToDto(string containerId, string name, string projectName, ContainerStatsResponse s, DateTime timestamp)
        {
            long rx = 0, tx = 0;
            foreach (var network in s.Networks?.Values ?? [])
            {
                rx += (long)network.RxBytes;
                tx += (long)network.TxBytes;
            }
            return new ContainerStatsDto(containerId, name, projectName, CpuPercent(s.CPUStats, s.PreCPUStats),
                MemoryUsed(s.MemoryStats), (long)(s.MemoryStats?.Limit ?? 0), rx, tx, timestamp);
        }

        /// <summary>Parses the docker CLI's human sizes ("0B", "45.1kB", "1.068MB", "8.085GB"; decimal units).
        /// Ignores a trailing " (86%)". Returns null for "N/A" or anything unreadable.</summary>
        public static long? ParseSize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var value = text.Split(' ', 2)[0].Trim();
            var unitStart = 0;
            while (unitStart < value.Length && (char.IsDigit(value[unitStart]) || value[unitStart] == '.')) unitStart++;
            if (unitStart == 0 || !double.TryParse(value[..unitStart], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return null;
            double factor = value[unitStart..].ToUpperInvariant() switch
            {
                "B" or "" => 1,
                "KB" => 1e3,
                "MB" => 1e6,
                "GB" => 1e9,
                "TB" => 1e12,
                "PB" => 1e15,
                _ => double.NaN,
            };
            return double.IsNaN(factor) ? null : (long)Math.Round(number * factor);
        }

        /// <summary>Reads `docker system df --format json` (one JSON object per line and type).</summary>
        public static DiskUsageDto ParseDiskUsage(string jsonLines)
        {
            long? imagesSize = null, imagesReclaimable = null, containersSize = null, volumesSize = null, volumesReclaimable = null, buildCache = null;
            foreach (var line in jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.StartsWith('{')) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                string? Get(string name) => root.TryGetProperty(name, out var p) ? p.GetString() : null;
                var size = ParseSize(Get("Size"));
                var reclaimable = ParseSize(Get("Reclaimable"));
                switch (Get("Type"))
                {
                    case "Images": imagesSize = size; imagesReclaimable = reclaimable; break;
                    case "Containers": containersSize = size; break;
                    case "Local Volumes": volumesSize = size; volumesReclaimable = reclaimable; break;
                    case "Build Cache": buildCache = size; break;
                }
            }
            return new DiskUsageDto(imagesSize, imagesReclaimable, containersSize, volumesSize, volumesReclaimable, buildCache);
        }

        /// <summary>Volume name -> size from `docker system df -v --format json` (the only place the daemon reports volume sizes).</summary>
        public static Dictionary<string, long> ParseVolumeSizes(string json)
        {
            var sizes = new Dictionary<string, long>();
            if (string.IsNullOrWhiteSpace(json)) return sizes;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Volumes", out var volumes) || volumes.ValueKind != JsonValueKind.Array)
                return sizes;
            foreach (var volume in volumes.EnumerateArray())
            {
                var name = volume.TryGetProperty("Name", out var n) ? n.GetString() : null;
                var size = volume.TryGetProperty("Size", out var s) ? ParseSize(s.GetString()) : null;
                if (name is not null && size is not null) sizes[name] = size.Value;
            }
            return sizes;
        }
    }
}
