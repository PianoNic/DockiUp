namespace DockiUp.Domain
{
    /// <summary>One downsampled (1-minute average) resource point of a container, kept for the history chart.
    /// High-volume and short-lived (pruned after the retention window), so it skips BaseEntity's Guid/audit columns.</summary>
    public class ContainerStatSample
    {
        public long Id { get; init; }
        /// <summary>Null = the control-plane host.</summary>
        public Guid? NodeId { get; init; }
        public required string ContainerId { get; init; }
        public required string ContainerName { get; init; }
        public required string ProjectName { get; init; }
        /// <summary>UTC start of the minute this point averages.</summary>
        public DateTime Timestamp { get; init; }
        public double CpuPercent { get; init; }
        public long MemoryUsage { get; init; }
        public long MemoryLimit { get; init; }
        public long NetworkRx { get; init; }
        public long NetworkTx { get; init; }
    }
}
