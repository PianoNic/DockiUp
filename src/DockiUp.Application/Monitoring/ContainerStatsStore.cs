using System.Collections.Concurrent;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Monitoring
{
    /// <summary>Live container stats (latest sample per container, in memory) plus the 1-minute downsampler that
    /// feeds the persisted history. Singleton: written by the stats sampler, read by the API.</summary>
    public class ContainerStatsStore
    {
        private readonly ConcurrentDictionary<string, ContainerStatsDto> _latest = new();
        private readonly StatsDownsampler _downsampler = new();
        private readonly Lock _gate = new();

        public IReadOnlyCollection<ContainerStatsDto> Latest => _latest.Values.ToArray();

        /// <summary>Replaces one host's live values with a fresh sampling round (containers that stopped drop
        /// out) and returns the minute points that this round completed, ready to persist.</summary>
        public IReadOnlyList<ContainerStatSample> Ingest(Guid? nodeId, IReadOnlyCollection<ContainerStatsDto> samples)
        {
            foreach (var key in _latest.Where(kv => kv.Value.NodeId == nodeId).Select(kv => kv.Key).ToList())
                _latest.TryRemove(key, out _);
            foreach (var sample in samples)
                _latest[Key(nodeId, sample.ContainerId)] = sample with { NodeId = nodeId };

            lock (_gate)
                return _downsampler.Add(samples.Select(s => s with { NodeId = nodeId }));
        }

        /// <summary>Drops a host's live values (it went offline), so the UI doesn't show stale numbers.</summary>
        public void Forget(Guid? nodeId)
        {
            foreach (var key in _latest.Where(kv => kv.Value.NodeId == nodeId).Select(kv => kv.Key).ToList())
                _latest.TryRemove(key, out _);
        }

        private static string Key(Guid? nodeId, string containerId) => $"{nodeId}/{containerId}";
    }

    /// <summary>Averages raw samples into one point per container and UTC minute. A minute's point is emitted
    /// once a sample from a later minute arrives for that container (or it is stale for over two minutes).</summary>
    public class StatsDownsampler
    {
        private sealed class Bucket
        {
            public required DateTime Minute;
            public required ContainerStatsDto Last;
            public double CpuSum;
            public double MemorySum;
            public int Count;
        }

        private readonly Dictionary<string, Bucket> _buckets = new();

        public static DateTime MinuteOf(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

        public List<ContainerStatSample> Add(IEnumerable<ContainerStatsDto> samples)
        {
            var done = new List<ContainerStatSample>();
            DateTime? newest = null;
            foreach (var s in samples)
            {
                var minute = MinuteOf(s.Timestamp);
                newest = newest is null || minute > newest ? minute : newest;
                var key = $"{s.NodeId}/{s.ContainerId}";
                if (_buckets.TryGetValue(key, out var bucket) && bucket.Minute != minute)
                {
                    done.Add(Close(bucket));
                    bucket = null;
                }
                bucket ??= _buckets[key] = new Bucket { Minute = minute, Last = s };
                bucket.Last = s;
                bucket.CpuSum += s.CpuPercent;
                bucket.MemorySum += s.MemoryUsage;
                bucket.Count++;
            }

            // A container that stopped never sends the "next minute" sample; close its bucket once it is stale.
            if (newest is { } now)
            {
                foreach (var (key, bucket) in _buckets.Where(b => b.Value.Minute < now.AddMinutes(-1)).ToList())
                {
                    done.Add(Close(bucket));
                    _buckets.Remove(key);
                }
            }
            return done;
        }

        private static ContainerStatSample Close(Bucket b) => new()
        {
            NodeId = b.Last.NodeId,
            ContainerId = b.Last.ContainerId,
            ContainerName = b.Last.ContainerName,
            ProjectName = b.Last.ProjectName,
            Timestamp = b.Minute,
            CpuPercent = Math.Round(b.CpuSum / b.Count, 2),
            MemoryUsage = (long)(b.MemorySum / b.Count),
            MemoryLimit = b.Last.MemoryLimit,
            // Network counters are cumulative since the container started; keep the latest reading.
            NetworkRx = b.Last.NetworkRx,
            NetworkTx = b.Last.NetworkTx,
        };
    }

    public static class ContainerStatsHistory
    {
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(24);

        /// <summary>Saves completed minute points and deletes points older than the retention window.</summary>
        public static async Task PersistAsync(IDockiUpDbContext db, IReadOnlyCollection<ContainerStatSample> points, DateTime nowUtc, TimeSpan retention, CancellationToken cancellationToken = default)
        {
            if (points.Count == 0) return;
            db.ContainerStatSamples.AddRange(points);
            var cutoff = nowUtc - retention;
            // Runs once a minute, so this only ever finds about a minute's worth of expired rows.
            db.ContainerStatSamples.RemoveRange(await db.ContainerStatSamples.Where(x => x.Timestamp < cutoff).ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
