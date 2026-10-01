using System.Text.Json.Serialization;

namespace DockiUp.Domain
{
    // String-serialized (also in the OpenAPI schema, so the generated client gets string unions).
    [JsonConverter(typeof(JsonStringEnumConverter<DeploymentStatus>))]
    public enum DeploymentStatus { Queued, Running, Succeeded, Failed }

    [JsonConverter(typeof(JsonStringEnumConverter<DeploymentTrigger>))]
    public enum DeploymentTrigger { Create, Manual, Webhook, Periodic, Rollback, ImageUpdate }

    /// <summary>One run of the deploy pipeline (git sync, compose pull, compose up) for a project.</summary>
    public class Deployment : BaseEntity
    {
        public required Guid ProjectId { get; init; }
        public required DeploymentTrigger Trigger { get; init; }
        public DeploymentStatus Status { get; set; } = DeploymentStatus.Queued;
        /// <summary>Who asked for it; null for background triggers (rendered "system").</summary>
        public string? ActorName { get; init; }
        public string? CommitBefore { get; set; }
        public string? CommitAfter { get; set; }
        /// <summary>Subject line of the deployed commit.</summary>
        public string? CommitMessage { get; set; }
        /// <summary>Set when a specific earlier commit was asked for (redeploy/rollback) instead of the branch tip.</summary>
        public string? TargetCommit { get; init; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string Log { get; set; } = string.Empty;
        public string? Error { get; set; }
    }
}
