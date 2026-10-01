using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Monitoring
{
    // ---- Container stats (#71) ----

    /// <summary>Latest live stats, optionally narrowed to one compose project or container (on one host).</summary>
    public sealed record GetContainerStatsQuery(string? DockerProjectName = null, string? ContainerId = null, Guid? NodeId = null) : IRequest<ContainerStatsDto[]>;

    public sealed class GetContainerStatsQueryHandler(ContainerStatsStore store) : IRequestHandler<GetContainerStatsQuery, ContainerStatsDto[]>
    {
        public ValueTask<ContainerStatsDto[]> Handle(GetContainerStatsQuery request, CancellationToken cancellationToken)
        {
            var narrowed = request.DockerProjectName is not null || request.ContainerId is not null;
            var stats = store.Latest
                .Where(s => !narrowed || s.NodeId == request.NodeId)
                .Where(s => request.DockerProjectName is null || string.Equals(s.ProjectName, request.DockerProjectName, StringComparison.OrdinalIgnoreCase))
                .Where(s => request.ContainerId is null || s.ContainerId == request.ContainerId || s.ContainerId.StartsWith(request.ContainerId, StringComparison.Ordinal))
                .OrderBy(s => s.ContainerName)
                .ToArray();
            return ValueTask.FromResult(stats);
        }
    }

    /// <summary>Minute points of one container over the last <see cref="Hours"/> hours (1-24). Keyed by container
    /// name, not id: compose keeps the name across redeploys while the id changes every time.</summary>
    public sealed record GetContainerStatsHistoryQuery(string ContainerName, Guid? NodeId = null, int Hours = 24) : IRequest<ContainerStatsPointDto[]>;

    public sealed class GetContainerStatsHistoryQueryHandler(IDockiUpDbContext db) : IRequestHandler<GetContainerStatsHistoryQuery, ContainerStatsPointDto[]>
    {
        public async ValueTask<ContainerStatsPointDto[]> Handle(GetContainerStatsHistoryQuery request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.ContainerName))
                throw new ArgumentException("A container name is required.");
            var since = DateTime.UtcNow.AddHours(-Math.Clamp(request.Hours, 1, 24));
            return await db.ContainerStatSamples
                .Where(x => x.ContainerName == request.ContainerName && x.NodeId == request.NodeId && x.Timestamp >= since)
                .OrderBy(x => x.Timestamp)
                .Select(x => new ContainerStatsPointDto(x.Timestamp, x.CpuPercent, x.MemoryUsage, x.MemoryLimit, x.NetworkRx, x.NetworkTx))
                .ToArrayAsync(cancellationToken);
        }
    }

    // ---- Images, volumes, networks (#73) ----

    public sealed record GetResourcesQuery(Guid? NodeId = null) : IRequest<DockerResourcesDto>;

    public sealed class GetResourcesQueryHandler(IDockerServiceResolver resolver) : IRequestHandler<GetResourcesQuery, DockerResourcesDto>
    {
        public async ValueTask<DockerResourcesDto> Handle(GetResourcesQuery request, CancellationToken cancellationToken)
            => await resolver.Resolve(request.NodeId).GetResourcesAsync(cancellationToken);
    }

    public sealed record RemoveResourceCommand(ResourceKind Kind, string Id, Guid? NodeId = null, string? DisplayName = null) : IRequest;

    public sealed class RemoveResourceCommandHandler(IDockerServiceResolver resolver, IDockiUpDbContext db, IActivityLogger activity) : IRequestHandler<RemoveResourceCommand>
    {
        public async ValueTask<Unit> Handle(RemoveResourceCommand request, CancellationToken cancellationToken)
        {
            await resolver.Resolve(request.NodeId).RemoveResourceAsync(request.Kind, request.Id, cancellationToken);
            var host = await HostNames.NameAsync(db, request.NodeId, cancellationToken);
            await activity.LogAsync($"{request.Kind.ToString().ToLowerInvariant()}.remove", request.DisplayName ?? request.Id, details: host, cancellationToken: cancellationToken);
            return default;
        }
    }

    public sealed record PruneResourcesCommand(PruneRequest Request, Guid? NodeId = null) : IRequest<PruneResultDto>;

    public sealed class PruneResourcesCommandHandler(IDockerServiceResolver resolver, IDockiUpDbContext db, IActivityLogger activity) : IRequestHandler<PruneResourcesCommand, PruneResultDto>
    {
        public async ValueTask<PruneResultDto> Handle(PruneResourcesCommand request, CancellationToken cancellationToken)
        {
            var result = await resolver.Resolve(request.NodeId).PruneAsync(request.Request, cancellationToken);
            var host = await HostNames.NameAsync(db, request.NodeId, cancellationToken);
            await activity.LogAsync("prune", host, details: CleanupScheduleRules.Describe(result), cancellationToken: cancellationToken);
            return result;
        }
    }

    // ---- Scheduled cleanup (#74) ----

    public sealed record GetCleanupScheduleQuery(Guid? NodeId = null) : IRequest<CleanupScheduleDto>;

    public sealed class GetCleanupScheduleQueryHandler(IDockiUpDbContext db) : IRequestHandler<GetCleanupScheduleQuery, CleanupScheduleDto>
    {
        public async ValueTask<CleanupScheduleDto> Handle(GetCleanupScheduleQuery request, CancellationToken cancellationToken)
            => CleanupScheduleRules.ToDto(await db.CleanupSchedules.FirstOrDefaultAsync(s => s.NodeId == request.NodeId, cancellationToken), request.NodeId);
    }

    public sealed record SaveCleanupScheduleCommand(Guid? NodeId, CleanupFrequency Frequency, int HourUtc, int DayOfWeekUtc, bool PruneVolumes) : IRequest<CleanupScheduleDto>;

    public sealed class SaveCleanupScheduleCommandHandler(IDockiUpDbContext db) : IRequestHandler<SaveCleanupScheduleCommand, CleanupScheduleDto>
    {
        public async ValueTask<CleanupScheduleDto> Handle(SaveCleanupScheduleCommand request, CancellationToken cancellationToken)
        {
            if (request.HourUtc is < 0 or > 23)
                throw new ArgumentException("The hour must be between 0 and 23.");
            if (request.DayOfWeekUtc is < 0 or > 6)
                throw new ArgumentException("The day of week must be between 0 (Sunday) and 6 (Saturday).");
            if (request.NodeId is { } nodeId && !await db.Nodes.AnyAsync(n => n.Id == nodeId, cancellationToken))
                throw new KeyNotFoundException($"Node {nodeId} not found.");

            var schedule = await db.CleanupSchedules.FirstOrDefaultAsync(s => s.NodeId == request.NodeId, cancellationToken);
            if (schedule is null)
            {
                schedule = new CleanupSchedule { NodeId = request.NodeId };
                db.CleanupSchedules.Add(schedule);
            }
            schedule.Frequency = request.Frequency;
            schedule.HourUtc = request.HourUtc;
            schedule.DayOfWeekUtc = request.DayOfWeekUtc;
            schedule.PruneVolumes = request.PruneVolumes;
            schedule.SavedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return CleanupScheduleRules.ToDto(schedule, request.NodeId);
        }
    }

    /// <summary>Runs one host's cleanup now (the scheduler, or "Run now" in the UI) with the scheduled
    /// keep-rule, records the outcome on the schedule and in the activity feed.</summary>
    public sealed record RunCleanupCommand(Guid? NodeId = null, bool Scheduled = false) : IRequest<PruneResultDto>;

    public sealed class RunCleanupCommandHandler(IDockerServiceResolver resolver, IDockiUpDbContext db, IActivityLogger activity, Mediator.IPublisher? publisher = null) : IRequestHandler<RunCleanupCommand, PruneResultDto>
    {
        public async ValueTask<PruneResultDto> Handle(RunCleanupCommand request, CancellationToken cancellationToken)
        {
            var schedule = await db.CleanupSchedules.FirstOrDefaultAsync(s => s.NodeId == request.NodeId, cancellationToken);
            if (schedule is null)
            {
                schedule = new CleanupSchedule { NodeId = request.NodeId };
                db.CleanupSchedules.Add(schedule);
            }
            var host = await HostNames.NameAsync(db, request.NodeId, cancellationToken);
            try
            {
                var result = await resolver.Resolve(request.NodeId).PruneAsync(CleanupScheduleRules.ScheduledRequest(schedule), cancellationToken);
                var summary = CleanupScheduleRules.Describe(result);
                schedule.LastRunAt = DateTime.UtcNow;
                schedule.LastRunResult = summary;
                await db.SaveChangesAsync(cancellationToken);
                await activity.LogAsync("cleanup", host, details: (request.Scheduled ? "Scheduled: " : "") + summary, cancellationToken: cancellationToken);
                // Notification channels subscribed to cleanup reports (#75).
                if (publisher is not null)
                    await publisher.Publish(new Notifications.CleanupCompleted(summary, result.SpaceReclaimed, host), cancellationToken);
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Recorded as a run, so a failing host isn't retried every minute until the next slot.
                schedule.LastRunAt = DateTime.UtcNow;
                schedule.LastRunResult = "failed: " + ex.Message;
                await db.SaveChangesAsync(cancellationToken);
                await activity.LogAsync("cleanup.failed", host, details: ex.Message, cancellationToken: cancellationToken);
                throw;
            }
        }
    }

    internal static class HostNames
    {
        public const string Local = "Local host";

        public static async Task<string> NameAsync(IDockiUpDbContext db, Guid? nodeId, CancellationToken cancellationToken)
            => nodeId is { } id
                ? await db.Nodes.Where(n => n.Id == id).Select(n => n.Name).FirstOrDefaultAsync(cancellationToken) ?? $"node {id}"
                : Local;
    }
}
