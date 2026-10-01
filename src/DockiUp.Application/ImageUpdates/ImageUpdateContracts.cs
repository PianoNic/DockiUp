using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Mediator;

namespace DockiUp.Application.ImageUpdates
{
    public record ImageUpdateDto(
        Guid ProjectId,
        string ServiceName,
        string Image,
        string? CurrentDigest,
        string? LatestDigest,
        DateTime CheckedAt,
        bool UpdateAvailable,
        string? Note)
    {
        public static ImageUpdateDto From(ImageUpdateStatus s)
            => new(s.ProjectId, s.ServiceName, s.Image, s.CurrentDigest, s.LatestDigest, s.CheckedAt, s.UpdateAvailable, s.Note);
    }

    /// <summary>A project's image update settings plus the tags pinned through dockiup.override.yml.</summary>
    public record ImageUpdateSettingsDto(ImageUpdatePolicy Policy, string[] ExcludedServices, Dictionary<string, string> PinnedImages);

    public record SetImageUpdateSettingsRequest(ImageUpdatePolicy Policy, string[]? ExcludedServices);

    public record PinServiceImageRequest(string Tag);

    /// <summary>Published (Mediator notification) when a check finds updates that were not known before -
    /// a new update, or a newer digest than the one last reported - for a project whose policy is Notify
    /// or Auto. Each update is reported once, not on every check. Subscribe with an
    /// <c>INotificationHandler&lt;ImageUpdatesFound&gt;</c> in this assembly (e.g. to send notifications);
    /// for Auto projects the ImageUpdate deployment is queued separately by the checker.</summary>
    public sealed record ImageUpdatesFound(Guid ProjectId, string ProjectName, ImageUpdatePolicy Policy, ImageUpdateDto[] Updates) : INotification;

    /// <summary>Records found updates in the activity feed.</summary>
    public sealed class ImageUpdatesFoundActivityHandler(Interfaces.IActivityLogger activity) : INotificationHandler<ImageUpdatesFound>
    {
        public async ValueTask Handle(ImageUpdatesFound notification, CancellationToken cancellationToken)
            => await activity.LogAsync("image.update", notification.ProjectName, notification.ProjectId,
                string.Join(", ", notification.Updates.Select(u => $"{u.ServiceName} ({u.Image})")), cancellationToken);
    }

    /// <summary>Pushes the current image update results to browsers (SignalR in the API).</summary>
    public interface IImageUpdateEvents
    {
        Task ChangedAsync(ImageUpdateDto[] updates);
    }
}
