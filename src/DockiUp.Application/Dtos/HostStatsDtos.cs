namespace DockiUp.Application.Dtos
{
    /// <summary>One reading of a whole host (the machine, not its containers). Rates are per second since the previous
    /// reading; a value the host can't report (no host view mounted, no GPU tool) is null, never 0.</summary>
    public record HostStatsDto
    {
        /// <summary>Null = the control-plane host. Set by the control plane, not by the host.</summary>
        public Guid? NodeId { get; init; }
        public DateTime Timestamp { get; init; }
        public string? Hostname { get; init; }
        public string? Os { get; init; }
        public string? Kernel { get; init; }
        public long UptimeSeconds { get; init; }
        public int Cores { get; init; }
        public double CpuPercent { get; init; }
        public double Load1 { get; init; }
        public double Load5 { get; init; }
        public double Load15 { get; init; }
        public long MemoryTotal { get; init; }
        /// <summary>Taken by programs (total minus available).</summary>
        public long MemoryUsed { get; init; }
        /// <summary>Page cache and reclaimable memory: in use, but given back when needed.</summary>
        public long MemoryCache { get; init; }
        public long SwapTotal { get; init; }
        public long SwapUsed { get; init; }
        public DiskStatsDto[] Disks { get; init; } = [];
        public double? NetworkRxRate { get; init; }
        public double? NetworkTxRate { get; init; }
        public double? DiskReadRate { get; init; }
        public double? DiskWriteRate { get; init; }
        /// <summary>The interface the default route uses, and its negotiated speed.</summary>
        public string? NetworkInterface { get; init; }
        public int? LinkSpeedMbps { get; init; }
        public GpuStatsDto[] Gpus { get; init; } = [];
        /// <summary>False when the host's root isn't mounted at /host: then disks, network and GPU processes are
        /// limited to what the container itself sees.</summary>
        public bool HostView { get; init; }
    }

    public record DiskStatsDto(string Mount, string? Device, long Total, long Used);

    /// <summary>A GPU of any vendor. Only what its driver reports is filled in.</summary>
    public record GpuStatsDto
    {
        public int Index { get; init; }
        public required string Name { get; init; }
        public required string Vendor { get; init; }
        public string? Driver { get; init; }
        public string? BusId { get; init; }
        public double? Utilization { get; init; }
        public long? MemoryUsed { get; init; }
        public long? MemoryTotal { get; init; }
        public double? Temperature { get; init; }
        public double? PowerDraw { get; init; }
        public double? PowerLimit { get; init; }
        public double? FanPercent { get; init; }
    }

    /// <summary>A process holding GPU memory, and the container it runs in (null: not a container).</summary>
    public record GpuProcessDto(int Pid, string Process, string? BusId, long MemoryUsed, string? ContainerId, string? ContainerName, string? ProjectName);

    /// <summary>An internet speed test run on the host itself. Error is set (and the numbers null) when it failed.</summary>
    public record SpeedTestDto(Guid? NodeId, DateTime At, double? DownloadMbps, double? UploadMbps, double? PingMs, double? JitterMs, string? Server, string? Error);
}
