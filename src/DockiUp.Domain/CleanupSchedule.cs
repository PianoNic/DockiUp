using DockiUp.Domain.Enums;

namespace DockiUp.Domain
{
    /// <summary>Scheduled prune of unused Docker resources on one host (NodeId null = the control-plane host).
    /// Times are UTC.</summary>
    public class CleanupSchedule : BaseEntity
    {
        public Guid? NodeId { get; init; }
        public CleanupFrequency Frequency { get; set; } = CleanupFrequency.Off;
        public int HourUtc { get; set; } = 3;
        public int DayOfWeekUtc { get; set; }
        public bool PruneVolumes { get; set; }
        /// <summary>When the schedule was last changed: runs due before that are not caught up.</summary>
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastRunAt { get; set; }
        public string? LastRunResult { get; set; }
    }
}
