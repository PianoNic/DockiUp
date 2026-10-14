namespace DockiUp.Domain
{
    /// <summary>One minute of a host's resources (averages), kept for the history charts. Like ContainerStatSample it
    /// skips BaseEntity: many small rows, pruned after a week. GPU values are over all of the host's GPUs.</summary>
    public class HostStatSample
    {
        public long Id { get; init; }
        /// <summary>Null = the control-plane host.</summary>
        public Guid? NodeId { get; init; }
        /// <summary>UTC start of the minute.</summary>
        public DateTime Timestamp { get; init; }
        public double CpuPercent { get; init; }
        public long MemoryUsed { get; init; }
        public long MemoryCache { get; init; }
        public long MemoryTotal { get; init; }
        public double? NetworkRxRate { get; init; }
        public double? NetworkTxRate { get; init; }
        public double? DiskReadRate { get; init; }
        public double? DiskWriteRate { get; init; }
        public double? GpuUtilization { get; init; }
        public long? GpuMemoryUsed { get; init; }
        public long? GpuMemoryTotal { get; init; }
    }

    /// <summary>An internet speed test of one host. Failed tests are kept too (with <see cref="Error"/>), they are
    /// part of the line's story.</summary>
    public class SpeedTestResult
    {
        public long Id { get; init; }
        public Guid? NodeId { get; init; }
        public DateTime At { get; init; }
        public double? DownloadMbps { get; init; }
        public double? UploadMbps { get; init; }
        public double? PingMs { get; init; }
        public double? JitterMs { get; init; }
        public string? Server { get; init; }
        public string? Error { get; init; }
    }
}
