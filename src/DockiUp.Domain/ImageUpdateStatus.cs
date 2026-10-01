namespace DockiUp.Domain
{
    /// <summary>Result of the last registry check for one service of a DockiUp project: the digest the
    /// running image was pulled by vs. the digest its tag points to now. One row per (project, service).</summary>
    public class ImageUpdateStatus : BaseEntity
    {
        public required Guid ProjectId { get; init; }
        public required string ServiceName { get; init; }
        /// <summary>Image reference the container runs, as written in compose (or the override).</summary>
        public required string Image { get; set; }
        /// <summary>Registry digest the local image was pulled by; null when built locally or unknown.</summary>
        public string? CurrentDigest { get; set; }
        /// <summary>Digest the tag points to in the registry right now.</summary>
        public string? LatestDigest { get; set; }
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
        public bool UpdateAvailable { get; set; }
        /// <summary>Why the service could not be compared (built locally, pinned by digest, registry error, ...).</summary>
        public string? Note { get; set; }
    }
}
