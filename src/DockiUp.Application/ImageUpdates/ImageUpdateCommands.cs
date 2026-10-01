using System.Text.RegularExpressions;
using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.ImageUpdates
{
    /// <summary>Compares every service image of every DockiUp project (local and on nodes) with its registry
    /// and stores the result; then applies each project's policy. <paramref name="ProjectId"/> limits the
    /// check to one project. Returns all stored results.</summary>
    public sealed record CheckImageUpdatesCommand(Guid? ProjectId = null) : IRequest<ImageUpdateDto[]>;

    public sealed class CheckImageUpdatesCommandHandler(
        IDockiUpDbContext db,
        IDockerServiceResolver dockerResolver,
        IRegistryClient registry,
        IMediator mediator,
        IImageUpdateEvents events)
        : IRequestHandler<CheckImageUpdatesCommand, ImageUpdateDto[]>
    {
        // The timer and "Check now" may overlap; one check at a time keeps the per-service rows consistent.
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public async ValueTask<ImageUpdateDto[]> Handle(CheckImageUpdatesCommand request, CancellationToken cancellationToken)
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                var projects = await db.ProjectInfo
                    .Where(p => request.ProjectId == null || p.Id == request.ProjectId)
                    .ToListAsync(cancellationToken);
                if (request.ProjectId is { } id && projects.Count == 0)
                    throw new KeyNotFoundException($"Project with id {id} not found.");

                // Many services share an image (and tag): ask the registry once per reference per check.
                var digests = new Dictionary<string, Task<string?>>();
                foreach (var project in projects)
                    await CheckProjectAsync(project, digests, cancellationToken);

                if (request.ProjectId is null)
                {
                    // Results of projects that were removed since.
                    var ids = projects.Select(p => p.Id).ToList();
                    db.ImageUpdates.RemoveRange(db.ImageUpdates.Where(u => !ids.Contains(u.ProjectId)));
                    await db.SaveChangesAsync(cancellationToken);
                }

                var all = (await db.ImageUpdates.ToListAsync(cancellationToken)).Select(ImageUpdateDto.From).ToArray();
                await events.ChangedAsync(all);
                return all;
            }
            finally { Gate.Release(); }
        }

        private async Task CheckProjectAsync(ProjectInfo project, Dictionary<string, Task<string?>> digests, CancellationToken cancellationToken)
        {
            var previous = await db.ImageUpdates.Where(u => u.ProjectId == project.Id).ToListAsync(cancellationToken);
            if (project.ImageUpdatePolicy == ImageUpdatePolicy.Off)
            {
                db.ImageUpdates.RemoveRange(previous);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            ServiceImageDto[] services;
            try
            {
                services = await dockerResolver.Resolve(project.NodeId).GetServiceImagesAsync(project.DockerProjectName, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return; // node offline / daemon unreachable: keep the last known results
            }

            var now = DateTime.UtcNow;
            var fresh = new List<ImageUpdateStatus>();
            var current = new List<ImageUpdateStatus>();
            foreach (var service in services)
            {
                var row = previous.FirstOrDefault(p => p.ServiceName == service.ServiceName);
                if (row is null)
                {
                    row = new ImageUpdateStatus { ProjectId = project.Id, ServiceName = service.ServiceName, Image = service.Image };
                    db.ImageUpdates.Add(row);
                }
                var before = (row.UpdateAvailable, row.LatestDigest);
                await CompareAsync(row, service, project.ImageUpdateExcludedServices.Contains(service.ServiceName), digests, cancellationToken);
                row.CheckedAt = now;
                current.Add(row);
                // Report each update once: when it appears, or when the tag moved on again since.
                if (row.UpdateAvailable && (!before.UpdateAvailable || before.LatestDigest != row.LatestDigest))
                    fresh.Add(row);
            }
            // Services that no longer run drop out.
            db.ImageUpdates.RemoveRange(previous.Where(p => services.All(s => s.ServiceName != p.ServiceName)));
            await db.SaveChangesAsync(cancellationToken);

            var anyAvailable = current.Any(r => r.UpdateAvailable);
            var pending = await db.Deployments.AnyAsync(d => d.ProjectId == project.Id && d.Trigger == DeploymentTrigger.ImageUpdate
                && (d.Status == DeploymentStatus.Queued || d.Status == DeploymentStatus.Running), cancellationToken);
            var (notify, deploy) = Decide(project.ImageUpdatePolicy, anyAvailable, fresh.Count > 0, pending);

            if (notify)
                await mediator.Publish(new ImageUpdatesFound(project.Id, project.ProjectName, project.ImageUpdatePolicy,
                    fresh.Select(ImageUpdateDto.From).ToArray()), cancellationToken);
            if (deploy)
                await mediator.Send(new QueueDeploymentCommand(project.Id, DeploymentTrigger.ImageUpdate), cancellationToken);
        }

        /// <summary>The policy decision: Off does nothing; Notify and Auto raise ImageUpdatesFound for newly
        /// found updates; Auto also queues an ImageUpdate deployment while any update is outstanding and none
        /// is already queued or running (so a failed one is retried on the next check).</summary>
        public static (bool Notify, bool Deploy) Decide(ImageUpdatePolicy policy, bool anyAvailable, bool anyNew, bool deploymentPending)
            => policy switch
            {
                ImageUpdatePolicy.Off => (false, false),
                ImageUpdatePolicy.Notify => (anyNew, false),
                _ => (anyNew, anyAvailable && !deploymentPending),
            };

        private async Task CompareAsync(ImageUpdateStatus row, ServiceImageDto service, bool excluded,
            Dictionary<string, Task<string?>> digests, CancellationToken cancellationToken)
        {
            row.Image = service.Image;
            row.UpdateAvailable = false;
            row.Note = null;
            row.CurrentDigest = null;
            row.LatestDigest = null;

            if (excluded) { row.Note = "Excluded from update checks"; return; }
            if (!ImageReference.TryParse(service.Image, out var image)) { row.Note = "Not a registry image reference"; return; }
            if (image.Digest is not null) { row.Note = "Pinned by digest"; return; }

            row.CurrentDigest = ImageDigests.CurrentDigest(image, service.RepoDigests);
            if (row.CurrentDigest is null) { row.Note = "No registry digest (built locally, or the tag has moved on: redeploy to use the newer image)"; return; }

            try
            {
                var key = image.ToString();
                if (!digests.TryGetValue(key, out var lookup))
                    digests[key] = lookup = registry.GetDigestAsync(image, cancellationToken);
                row.LatestDigest = await lookup;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.Note = ex.Message;
                return;
            }

            if (row.LatestDigest is null) row.Note = $"Tag '{image.Tag}' not found in the registry";
            else row.UpdateAvailable = ImageDigests.IsNewer(row.LatestDigest, service.RepoDigests);
        }
    }

    public sealed record ListImageUpdatesQuery : IRequest<ImageUpdateDto[]>;

    public sealed class ListImageUpdatesQueryHandler(IDockiUpDbContext db) : IRequestHandler<ListImageUpdatesQuery, ImageUpdateDto[]>
    {
        public async ValueTask<ImageUpdateDto[]> Handle(ListImageUpdatesQuery request, CancellationToken cancellationToken)
            => (await db.ImageUpdates.ToListAsync(cancellationToken)).Select(ImageUpdateDto.From).ToArray();
    }

    public sealed record GetImageUpdateSettingsQuery(Guid ProjectId) : IRequest<ImageUpdateSettingsDto>;

    public sealed class GetImageUpdateSettingsQueryHandler(IDockiUpDbContext db, IDockerServiceResolver dockerResolver)
        : IRequestHandler<GetImageUpdateSettingsQuery, ImageUpdateSettingsDto>
    {
        public async ValueTask<ImageUpdateSettingsDto> Handle(GetImageUpdateSettingsQuery request, CancellationToken cancellationToken)
        {
            var project = await ImageUpdateProjects.FindAsync(db, request.ProjectId, cancellationToken);
            Dictionary<string, string> pinned;
            try
            {
                pinned = await dockerResolver.Resolve(project.NodeId).GetImageOverridesAsync(project.ProjectPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ArgumentException)
            {
                pinned = []; // node offline: settings still load, pins show again once it is back
            }
            return new ImageUpdateSettingsDto(project.ImageUpdatePolicy, [.. project.ImageUpdateExcludedServices], pinned);
        }
    }

    public sealed record SetImageUpdateSettingsCommand(Guid ProjectId, ImageUpdatePolicy Policy, string[] ExcludedServices, string? ActorName = null) : IRequest;

    public sealed class SetImageUpdateSettingsCommandHandler(IDockiUpDbContext db, IActivityLogger activity)
        : IRequestHandler<SetImageUpdateSettingsCommand>
    {
        public async ValueTask<Unit> Handle(SetImageUpdateSettingsCommand request, CancellationToken cancellationToken)
        {
            if (!Enum.IsDefined(request.Policy)) throw new ArgumentException("Unknown image update policy.");
            var project = await ImageUpdateProjects.FindAsync(db, request.ProjectId, cancellationToken);
            var excluded = request.ExcludedServices.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
            if (excluded.FirstOrDefault(s => !ImageUpdateProjects.IsServiceName(s)) is { } bad)
                throw new ArgumentException($"'{bad}' is not a valid service name.");

            project.ImageUpdatePolicy = request.Policy;
            project.ImageUpdateExcludedServices = excluded;
            // An excluded service or a switched-off project stops counting right away, not at the next check.
            var stale = await db.ImageUpdates.Where(u => u.ProjectId == project.Id).ToListAsync(cancellationToken);
            if (request.Policy == ImageUpdatePolicy.Off) db.ImageUpdates.RemoveRange(stale);
            else
                foreach (var row in stale.Where(r => excluded.Contains(r.ServiceName)))
                {
                    row.UpdateAvailable = false;
                    row.LatestDigest = null;
                    row.Note = "Excluded from update checks";
                }
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("image.policy", project.ProjectName, project.Id,
                excluded.Count > 0 ? $"{request.Policy} (excluding {string.Join(", ", excluded)})" : request.Policy.ToString(),
                cancellationToken, request.ActorName);
            return default;
        }
    }

    /// <summary>The registry's tags for the image a service runs, newest first (heuristic, see
    /// <see cref="ImageDigests.NewestFirst"/>). The image comes from the project, never from the caller.</summary>
    public sealed record ListServiceTagsQuery(Guid ProjectId, string ServiceName) : IRequest<string[]>;

    public sealed class ListServiceTagsQueryHandler(IDockiUpDbContext db, IDockerServiceResolver dockerResolver, IRegistryClient registry)
        : IRequestHandler<ListServiceTagsQuery, string[]>
    {
        public async ValueTask<string[]> Handle(ListServiceTagsQuery request, CancellationToken cancellationToken)
        {
            var project = await ImageUpdateProjects.FindAsync(db, request.ProjectId, cancellationToken);
            var image = await ImageUpdateProjects.ServiceImageAsync(db, dockerResolver, project, request.ServiceName, cancellationToken);
            return [.. ImageDigests.NewestFirst(await registry.ListTagsAsync(image, cancellationToken: cancellationToken))];
        }
    }

    /// <summary>Pins a service to another tag of its image through dockiup.override.yml (null tag: remove
    /// the pin, back to the compose file's image), then queues a deployment that applies it.</summary>
    public sealed record PinServiceImageCommand(Guid ProjectId, string ServiceName, string? Tag, string? ActorName = null) : IRequest<DeploymentDto>;

    public sealed class PinServiceImageCommandHandler(IDockiUpDbContext db, IDockerServiceResolver dockerResolver, IMediator mediator, IActivityLogger activity)
        : IRequestHandler<PinServiceImageCommand, DeploymentDto>
    {
        public async ValueTask<DeploymentDto> Handle(PinServiceImageCommand request, CancellationToken cancellationToken)
        {
            var project = await ImageUpdateProjects.FindAsync(db, request.ProjectId, cancellationToken);
            if (!ImageUpdateProjects.IsServiceName(request.ServiceName))
                throw new ArgumentException($"'{request.ServiceName}' is not a valid service name.");
            var docker = dockerResolver.Resolve(project.NodeId);

            string? pinned = null;
            if (request.Tag is not null)
            {
                var tag = request.Tag.Trim();
                if (!ImageReference.IsValidTag(tag)) throw new ArgumentException($"'{tag}' is not a valid image tag.");
                var image = await ImageUpdateProjects.ServiceImageAsync(db, dockerResolver, project, request.ServiceName, cancellationToken);
                pinned = image.WithTag(tag).ToString();
            }
            await docker.SetImageOverrideAsync(project.ProjectPath, request.ServiceName, pinned);

            await activity.LogAsync(pinned is null ? "image.reset" : "image.pin", project.ProjectName, project.Id,
                pinned is null ? request.ServiceName : $"{request.ServiceName} -> {pinned}", cancellationToken, request.ActorName);
            return await mediator.Send(new QueueDeploymentCommand(project.Id, DeploymentTrigger.Manual, request.ActorName), cancellationToken);
        }
    }

    internal static partial class ImageUpdateProjects
    {
        // Compose service names: letters, digits, dot, dash, underscore.
        [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
        private static partial Regex ServiceName();

        public static bool IsServiceName(string? name) => name is not null && ServiceName().IsMatch(name);

        public static async Task<ProjectInfo> FindAsync(IDockiUpDbContext db, Guid id, CancellationToken cancellationToken)
            => await db.ProjectInfo.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
               ?? throw new KeyNotFoundException($"Project with id {id} not found.");

        /// <summary>The image a service runs: from the last check, else asked from the host.</summary>
        public static async Task<ImageReference> ServiceImageAsync(IDockiUpDbContext db, IDockerServiceResolver dockerResolver,
            ProjectInfo project, string serviceName, CancellationToken cancellationToken)
        {
            var reference = (await db.ImageUpdates.FirstOrDefaultAsync(u => u.ProjectId == project.Id && u.ServiceName == serviceName, cancellationToken))?.Image
                ?? (await dockerResolver.Resolve(project.NodeId).GetServiceImagesAsync(project.DockerProjectName, cancellationToken))
                    .FirstOrDefault(s => s.ServiceName == serviceName)?.Image
                ?? throw new KeyNotFoundException($"Service '{serviceName}' is not running in {project.ProjectName}.");
            if (!ImageReference.TryParse(reference, out var image))
                throw new ArgumentException($"'{reference}' is not a registry image, so it has no tags to choose from.");
            return image;
        }
    }
}
