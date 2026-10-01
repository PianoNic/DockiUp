using DockiUp.Domain.Enums;

namespace DockiUp.Application.Dtos
{
    /// <summary>One live resource sample of a running container. NodeId is filled in by the control plane
    /// (null = the control-plane host). Memory is the working set (cache excluded), like `docker stats`.</summary>
    public record ContainerStatsDto(
        string ContainerId,
        string ContainerName,
        string ProjectName,
        double CpuPercent,
        long MemoryUsage,
        long MemoryLimit,
        long NetworkRx,
        long NetworkTx,
        DateTime Timestamp,
        Guid? NodeId = null);

    /// <summary>One downsampled history point (1-minute average) of a container.</summary>
    public record ContainerStatsPointDto(DateTime Timestamp, double CpuPercent, long MemoryUsage, long MemoryLimit, long NetworkRx, long NetworkTx);

    /// <summary>What to read from a container's log. Tail &lt;= 0 means the whole log.</summary>
    public record ContainerLogOptions(int? Tail = 100, bool Stdout = true, bool Stderr = true, bool Timestamps = false);

    public record DockerImageDto(string Id, string[] Tags, long Size, DateTime Created, int Containers, bool InUse, bool Dangling);

    /// <summary>Size is null when the daemon didn't report it (it needs `docker system df -v`).</summary>
    public record DockerVolumeDto(string Name, string Driver, long? Size, DateTime? Created, int Containers, bool InUse);

    /// <summary>Builtin is true for bridge/host/none, which can't be removed.</summary>
    public record DockerNetworkDto(string Id, string Name, string Driver, string Scope, int Containers, bool InUse, bool Builtin);

    /// <summary>Space per resource type (bytes) and how much of it a prune could free; null when unknown.</summary>
    public record DiskUsageDto(long? ImagesSize, long? ImagesReclaimable, long? ContainersSize, long? VolumesSize, long? VolumesReclaimable, long? BuildCacheSize);

    public record DockerResourcesDto(DockerImageDto[] Images, DockerVolumeDto[] Volumes, DockerNetworkDto[] Networks, DiskUsageDto DiskUsage);

    // String-serialized (also over SignalR to nodes and in the OpenAPI schema).
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ResourceKind>))]
    public enum ResourceKind { Image, Volume, Network }

    /// <summary>Which unused resources to prune on one host.</summary>
    /// <param name="AllImages">false = only dangling (untagged) images; true = every image no container uses.</param>
    /// <param name="KeepProjectContainers">Skip stopped containers that belong to a compose project, so a
    /// stopped project keeps its containers - and through them its images and volumes - for the next start.</param>
    public record PruneRequest(
        bool Containers = false,
        bool Images = false,
        bool AllImages = false,
        bool Networks = false,
        bool Volumes = false,
        bool KeepProjectContainers = false);

    public record PruneResultDto(int ContainersDeleted, int ImagesDeleted, int VolumesDeleted, int NetworksDeleted, long SpaceReclaimed);

    /// <summary>Scheduled cleanup of one host. Times are UTC: HourUtc 0-23, DayOfWeekUtc 0 (Sunday) - 6, used when weekly.</summary>
    public record CleanupScheduleDto(Guid? NodeId, CleanupFrequency Frequency, int HourUtc, int DayOfWeekUtc, bool PruneVolumes, DateTime? LastRunAt, string? LastRunResult);
}
