using System.Collections.Concurrent;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Monitoring
{
    /// <summary>A point of a host's history. GPU values are over all of its GPUs.</summary>
    public record HostStatPointDto(DateTime Timestamp, double CpuPercent, long MemoryUsed, long MemoryCache, long MemoryTotal,
        double? NetworkRxRate, double? NetworkTxRate, double? DiskReadRate, double? DiskWriteRate,
        double? GpuUtilization, long? GpuMemoryUsed, long? GpuMemoryTotal)
    {
        public static HostStatPointDto From(HostStatSample s) => new(s.Timestamp, s.CpuPercent, s.MemoryUsed, s.MemoryCache, s.MemoryTotal,
            s.NetworkRxRate, s.NetworkTxRate, s.DiskReadRate, s.DiskWriteRate, s.GpuUtilization, s.GpuMemoryUsed, s.GpuMemoryTotal);
    }

    /// <summary>What the Nodes page and the dashboard show per host: the live reading, the last hour and the latest speed test.</summary>
    public record HostSummaryDto(Guid? NodeId, HostStatsDto? Stats, HostStatPointDto[] LastHour, SpeedTestDto? LastSpeedTest);

    /// <summary>The latest reading of every host (in memory) and the minute averages that feed the history. Singleton.</summary>
    public class HostStatsStore
    {
        private sealed class Minute
        {
            public required DateTime Start;
            public required HostStatsDto Last;
            public readonly List<HostStatsDto> Readings = [];
        }

        private readonly ConcurrentDictionary<string, HostStatsDto> _latest = new();
        private readonly Dictionary<string, Minute> _minutes = new();
        private readonly Lock _gate = new();

        public IReadOnlyCollection<HostStatsDto> Latest => _latest.Values.ToArray();

        public HostStatsDto? Get(Guid? nodeId) => _latest.GetValueOrDefault(Key(nodeId));

        /// <summary>Stores a reading and returns the minute it completed, if this reading started a new one.</summary>
        public HostStatSample? Ingest(Guid? nodeId, HostStatsDto stats)
        {
            stats = stats with { NodeId = nodeId };
            _latest[Key(nodeId)] = stats;
            var start = StatsDownsampler.MinuteOf(stats.Timestamp);
            lock (_gate)
            {
                HostStatSample? done = null;
                if (_minutes.TryGetValue(Key(nodeId), out var minute) && minute.Start != start)
                {
                    done = Average(nodeId, minute);
                    minute = null;
                }
                minute ??= _minutes[Key(nodeId)] = new Minute { Start = start, Last = stats };
                minute.Readings.Add(stats);
                minute.Last = stats;
                return done;
            }
        }

        /// <summary>A host went offline: no stale numbers.</summary>
        public void Forget(Guid? nodeId)
        {
            _latest.TryRemove(Key(nodeId), out _);
            lock (_gate) _minutes.Remove(Key(nodeId));
        }

        public static HostStatSample Average(Guid? nodeId, IReadOnlyCollection<HostStatsDto> readings, DateTime start)
        {
            double? Avg(Func<HostStatsDto, double?> f) => readings.Select(f).OfType<double>().DefaultIfEmpty(double.NaN).Average() is var a && !double.IsNaN(a) ? Math.Round(a, 1) : null;
            var gpus = readings.Where(r => r.Gpus.Length > 0).ToList();
            return new HostStatSample
            {
                NodeId = nodeId,
                Timestamp = start,
                CpuPercent = Avg(r => r.CpuPercent) ?? 0,
                MemoryUsed = (long)readings.Average(r => r.MemoryUsed),
                MemoryCache = (long)readings.Average(r => r.MemoryCache),
                MemoryTotal = readings.Last().MemoryTotal,
                NetworkRxRate = Avg(r => r.NetworkRxRate),
                NetworkTxRate = Avg(r => r.NetworkTxRate),
                DiskReadRate = Avg(r => r.DiskReadRate),
                DiskWriteRate = Avg(r => r.DiskWriteRate),
                GpuUtilization = gpus.Count == 0 ? null : Avg(r => GpuLoad(r.Gpus)),
                GpuMemoryUsed = gpus.Count == 0 ? null : (long?)gpus.Average(r => r.Gpus.Sum(g => g.MemoryUsed ?? 0)),
                GpuMemoryTotal = gpus.Count == 0 ? null : gpus.Last().Gpus.Sum(g => g.MemoryTotal ?? 0) is var t && t > 0 ? t : null,
            };
        }

        /// <summary>Average load over the GPUs that report one; null when none does.</summary>
        public static double? GpuLoad(IEnumerable<GpuStatsDto> gpus)
            => gpus.Select(g => g.Utilization).OfType<double>().DefaultIfEmpty(double.NaN).Average() is var a && !double.IsNaN(a) ? a : null;

        private static HostStatSample Average(Guid? nodeId, Minute m) => Average(nodeId, m.Readings, m.Start);

        private static string Key(Guid? nodeId) => nodeId?.ToString() ?? "local";
    }

    public static class HostStatsHistory
    {
        public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
        public static readonly TimeSpan SpeedTestRetention = TimeSpan.FromDays(30);
        private const int MaxPoints = 360;

        public static async Task PersistAsync(IDockiUpDbContext db, HostStatSample point, CancellationToken cancellationToken = default)
        {
            db.HostStatSamples.Add(point);
            var cutoff = point.Timestamp - Retention;
            db.HostStatSamples.RemoveRange(await db.HostStatSamples.Where(x => x.Timestamp < cutoff).ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
        }

        /// <summary>A host's history over the last <paramref name="hours"/> (1 to 168), averaged into about
        /// <see cref="MaxPoints"/> points so a week stays as light as an hour.</summary>
        public static async Task<HostStatPointDto[]> QueryAsync(IDockiUpDbContext db, Guid? nodeId, int hours, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var since = nowUtc.AddHours(-Math.Clamp(hours, 1, 168));
            var rows = await db.HostStatSamples.AsNoTracking()
                .Where(x => x.NodeId == nodeId && x.Timestamp >= since)
                .OrderBy(x => x.Timestamp)
                .ToListAsync(cancellationToken);
            var minutesPerPoint = Math.Max(1, (int)Math.Ceiling(hours * 60.0 / MaxPoints));
            if (minutesPerPoint == 1) return [.. rows.Select(HostStatPointDto.From)];
            return [.. rows
                .GroupBy(r => r.Timestamp.Ticks / (TimeSpan.TicksPerMinute * minutesPerPoint))
                .Select(g => HostStatPointDto.From(Merge(g.ToList())))];
        }

        private static HostStatSample Merge(List<HostStatSample> g)
        {
            double? Avg(Func<HostStatSample, double?> f) => g.Select(f).OfType<double>().DefaultIfEmpty(double.NaN).Average() is var a && !double.IsNaN(a) ? Math.Round(a, 1) : null;
            long? AvgL(Func<HostStatSample, long?> f) => g.Select(f).OfType<long>().ToList() is { Count: > 0 } v ? (long)v.Average() : null;
            return new HostStatSample
            {
                NodeId = g[0].NodeId, Timestamp = g[0].Timestamp,
                CpuPercent = Avg(x => x.CpuPercent) ?? 0,
                MemoryUsed = AvgL(x => x.MemoryUsed) ?? 0, MemoryCache = AvgL(x => x.MemoryCache) ?? 0, MemoryTotal = g[^1].MemoryTotal,
                NetworkRxRate = Avg(x => x.NetworkRxRate), NetworkTxRate = Avg(x => x.NetworkTxRate),
                DiskReadRate = Avg(x => x.DiskReadRate), DiskWriteRate = Avg(x => x.DiskWriteRate),
                GpuUtilization = Avg(x => x.GpuUtilization), GpuMemoryUsed = AvgL(x => x.GpuMemoryUsed), GpuMemoryTotal = g[^1].GpuMemoryTotal,
            };
        }

        public static async Task<SpeedTestDto> SaveSpeedTestAsync(IDockiUpDbContext db, Guid? nodeId, SpeedTestDto test, CancellationToken cancellationToken = default)
        {
            db.SpeedTestResults.Add(new SpeedTestResult
            {
                NodeId = nodeId, At = test.At, DownloadMbps = test.DownloadMbps, UploadMbps = test.UploadMbps,
                PingMs = test.PingMs, JitterMs = test.JitterMs, Server = test.Server, Error = test.Error,
            });
            var cutoff = test.At - SpeedTestRetention;
            db.SpeedTestResults.RemoveRange(await db.SpeedTestResults.Where(x => x.At < cutoff).ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
            return test with { NodeId = nodeId };
        }

        public static SpeedTestDto ToDto(SpeedTestResult r) => new(r.NodeId, r.At, r.DownloadMbps, r.UploadMbps, r.PingMs, r.JitterMs, r.Server, r.Error);
    }
}
