using DockiUp.Domain;

namespace DockiUp.Application.Deployments
{
    public record DeploymentDto(
        Guid Id,
        Guid ProjectId,
        DeploymentTrigger Trigger,
        DeploymentStatus Status,
        string? ActorName,
        string? CommitBefore,
        string? CommitAfter,
        string? CommitMessage,
        string? TargetCommit,
        DateTime CreatedAt,
        DateTime? StartedAt,
        DateTime? FinishedAt,
        string? Error,
        /// <summary>Only filled when fetching a single deployment; lists leave it null.</summary>
        string? Log = null)
    {
        public static DeploymentDto From(Deployment d, bool withLog = false) => new(
            d.Id, d.ProjectId, d.Trigger, d.Status, d.ActorName, d.CommitBefore, d.CommitAfter, d.CommitMessage, d.TargetCommit,
            d.CreatedAt, d.StartedAt, d.FinishedAt, d.Error, withLog ? d.Log : null);
    }

    /// <summary>A unit of work for the deploy queue. <see cref="DeploymentId"/> is set when a Queued row
    /// was created up front (manual, webhook, create); periodic checks have none until they find work.</summary>
    public record DeploymentRequest(Guid ProjectId, DeploymentTrigger Trigger, Guid? DeploymentId = null, string? ActorName = null, string? TargetCommit = null);

    public interface IDeploymentQueue
    {
        /// <summary>False when that project already has a periodic check waiting (no pile-up).</summary>
        bool TryEnqueue(DeploymentRequest request);
    }

    /// <summary>Pushes deployment progress to browsers (SignalR in the API).</summary>
    public interface IDeploymentEvents
    {
        Task ChangedAsync(DeploymentDto deployment);
        Task LogAsync(Guid deploymentId, string line);
    }
}
