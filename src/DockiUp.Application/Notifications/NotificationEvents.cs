using DockiUp.Domain;
using DockiUp.Application.ImageUpdates;
using Mediator;

namespace DockiUp.Application.Notifications
{
    // Things that happened, published with Mediator's IPublisher by whichever feature notices them. The
    // handler below turns each into a message and queues it; channels are sent off the request path.

    /// <summary>Published by the deploy pipeline when a deployment finished (either way).</summary>
    public sealed record DeploymentFinished(Guid DeploymentId, Guid ProjectId, string ProjectName, bool Succeeded,
        string Trigger, string? Commit, string? Error) : INotification;

    /// <summary>Published when a node connects to or drops from the control plane.</summary>
    public sealed record NodeConnectionChanged(Guid NodeId, string NodeName, bool Online) : INotification;

    /// <summary>Publish after a cleanup (prune) run with a human-readable summary of what was removed.</summary>
    public sealed record CleanupCompleted(string Summary, long ReclaimedBytes = 0, string? NodeName = null) : INotification;

    /// <summary>What a channel sends. <see cref="Data"/> is the structured form for generic webhooks.</summary>
    public sealed record NotificationMessage(NotificationEvent Event, string Title, string Body, IReadOnlyDictionary<string, string?>? Data = null);

    /// <summary>Queues a message for every enabled channel subscribed to its event; returns immediately.</summary>
    public interface INotificationDispatcher
    {
        void Enqueue(NotificationMessage message);
    }

    /// <summary>A channel's settings with its credential decrypted, ready to send.</summary>
    public sealed record ChannelTarget(NotificationChannelType Type, string? Url, string? Target, string? HeaderName, string? Secret)
    {
        // Keep the decrypted credential out of any log that formats this record.
        public override string ToString() => $"ChannelTarget {{ Type = {Type}, Url = {Url}, Target = {Target} }}";
    }

    /// <summary>Formats and posts one message to one channel. Throws on failure (callers decide whether to log or report).</summary>
    public interface INotificationSender
    {
        Task SendAsync(ChannelTarget channel, NotificationMessage message, CancellationToken cancellationToken);
    }

    public sealed class NotificationEventHandler(INotificationDispatcher dispatcher)
        : INotificationHandler<DeploymentFinished>, INotificationHandler<NodeConnectionChanged>,
          INotificationHandler<ImageUpdatesFound>, INotificationHandler<CleanupCompleted>
    {
        public ValueTask Handle(DeploymentFinished e, CancellationToken cancellationToken)
        {
            var commit = e.Commit is { } sha ? $" @ {sha[..Math.Min(7, sha.Length)]}" : "";
            dispatcher.Enqueue(e.Succeeded
                ? new NotificationMessage(NotificationEvent.DeploymentSucceeded, $"Deployed {e.ProjectName}",
                    $"{e.ProjectName} deployed successfully{commit} ({e.Trigger}).", Data(e))
                : new NotificationMessage(NotificationEvent.DeploymentFailed, $"Deployment of {e.ProjectName} failed",
                    $"{e.ProjectName} failed to deploy{commit} ({e.Trigger}): {e.Error}", Data(e)));
            return ValueTask.CompletedTask;
        }

        public ValueTask Handle(NodeConnectionChanged e, CancellationToken cancellationToken)
        {
            dispatcher.Enqueue(e.Online
                ? new NotificationMessage(NotificationEvent.NodeOnline, $"Node {e.NodeName} is online", $"Node {e.NodeName} connected.",
                    new Dictionary<string, string?> { ["nodeId"] = e.NodeId.ToString(), ["nodeName"] = e.NodeName })
                : new NotificationMessage(NotificationEvent.NodeOffline, $"Node {e.NodeName} is offline", $"Node {e.NodeName} disconnected.",
                    new Dictionary<string, string?> { ["nodeId"] = e.NodeId.ToString(), ["nodeName"] = e.NodeName }));
            return ValueTask.CompletedTask;
        }

        // Raised by the image update check (#68) for newly found updates, under the Notify and Auto policies.
        public ValueTask Handle(ImageUpdatesFound e, CancellationToken cancellationToken)
        {
            var images = e.Updates.Select(u => $"{u.ServiceName} ({u.Image})").ToArray();
            var auto = e.Policy == Domain.Enums.ImageUpdatePolicy.Auto ? " They are being deployed automatically." : "";
            dispatcher.Enqueue(new NotificationMessage(NotificationEvent.ImageUpdateAvailable, $"Image updates for {e.ProjectName}",
                $"Newer images are available for {e.ProjectName}: {string.Join(", ", images)}.{auto}",
                new Dictionary<string, string?> { ["projectId"] = e.ProjectId.ToString(), ["projectName"] = e.ProjectName, ["images"] = string.Join(",", images) }));
            return ValueTask.CompletedTask;
        }

        public ValueTask Handle(CleanupCompleted e, CancellationToken cancellationToken)
        {
            var where = e.NodeName is null ? "" : $" on {e.NodeName}";
            dispatcher.Enqueue(new NotificationMessage(NotificationEvent.CleanupReport, $"Cleanup finished{where}", e.Summary,
                new Dictionary<string, string?> { ["nodeName"] = e.NodeName, ["reclaimedBytes"] = e.ReclaimedBytes.ToString() }));
            return ValueTask.CompletedTask;
        }

        private static Dictionary<string, string?> Data(DeploymentFinished e) => new()
        {
            ["deploymentId"] = e.DeploymentId.ToString(),
            ["projectId"] = e.ProjectId.ToString(),
            ["projectName"] = e.ProjectName,
            ["trigger"] = e.Trigger,
            ["commit"] = e.Commit,
            ["error"] = e.Error,
        };
    }
}
