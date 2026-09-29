using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Deployments
{
    /// <summary>Records a Queued deployment and hands it to the deploy queue. Returns immediately.
    /// <paramref name="TargetCommit"/> redeploys that exact commit instead of the branch tip.</summary>
    public sealed record QueueDeploymentCommand(Guid ProjectId, DeploymentTrigger Trigger, string? ActorName = null, string? TargetCommit = null) : IRequest<DeploymentDto>;

    public sealed class QueueDeploymentCommandHandler(IDockiUpDbContext db, IDeploymentQueue queue, IDeploymentEvents events)
        : IRequestHandler<QueueDeploymentCommand, DeploymentDto>
    {
        public async ValueTask<DeploymentDto> Handle(QueueDeploymentCommand request, CancellationToken cancellationToken)
        {
            if (!await db.ProjectInfo.AnyAsync(p => p.Id == request.ProjectId, cancellationToken))
                throw new KeyNotFoundException($"Project with id {request.ProjectId} not found.");

            var deployment = new Deployment { ProjectId = request.ProjectId, Trigger = request.Trigger, ActorName = request.ActorName, TargetCommit = request.TargetCommit };
            db.Deployments.Add(deployment);
            await db.SaveChangesAsync(cancellationToken);

            var dto = DeploymentDto.From(deployment);
            await events.ChangedAsync(dto);
            queue.TryEnqueue(new DeploymentRequest(request.ProjectId, request.Trigger, deployment.Id, request.ActorName, request.TargetCommit));
            return dto;
        }
    }

    public sealed record ListDeploymentsQuery(Guid ProjectId, int Limit = 20) : IRequest<DeploymentDto[]>;

    public sealed class ListDeploymentsQueryHandler(IDockiUpDbContext db) : IRequestHandler<ListDeploymentsQuery, DeploymentDto[]>
    {
        public async ValueTask<DeploymentDto[]> Handle(ListDeploymentsQuery request, CancellationToken cancellationToken)
        {
            var rows = await db.Deployments
                .Where(d => d.ProjectId == request.ProjectId)
                .OrderByDescending(d => d.CreatedAt)
                .Take(Math.Clamp(request.Limit, 1, 100))
                .ToListAsync(cancellationToken);
            return rows.Select(d => DeploymentDto.From(d)).ToArray();
        }
    }

    public sealed record GetDeploymentQuery(Guid DeploymentId) : IRequest<DeploymentDto>;

    public sealed class GetDeploymentQueryHandler(IDockiUpDbContext db) : IRequestHandler<GetDeploymentQuery, DeploymentDto>
    {
        public async ValueTask<DeploymentDto> Handle(GetDeploymentQuery request, CancellationToken cancellationToken)
        {
            var deployment = await db.Deployments.FirstOrDefaultAsync(d => d.Id == request.DeploymentId, cancellationToken)
                ?? throw new KeyNotFoundException($"Deployment {request.DeploymentId} not found.");
            return DeploymentDto.From(deployment, withLog: true);
        }
    }

    /// <summary>A deployment with the project it belongs to, for app-wide views (the header task list).</summary>
    public record DeploymentTaskDto(DeploymentDto Deployment, string ProjectName, string DockerProjectName);

    /// <summary>Everything queued or running, plus the most recent finished ones - newest first.</summary>
    public sealed record ListDeploymentTasksQuery(int RecentLimit = 8) : IRequest<DeploymentTaskDto[]>;

    public sealed class ListDeploymentTasksQueryHandler(IDockiUpDbContext db) : IRequestHandler<ListDeploymentTasksQuery, DeploymentTaskDto[]>
    {
        public async ValueTask<DeploymentTaskDto[]> Handle(ListDeploymentTasksQuery request, CancellationToken cancellationToken)
        {
            var open = await db.Deployments
                .Where(d => d.Status == DeploymentStatus.Queued || d.Status == DeploymentStatus.Running)
                .ToListAsync(cancellationToken);
            var recent = await db.Deployments
                .Where(d => d.Status == DeploymentStatus.Succeeded || d.Status == DeploymentStatus.Failed)
                .OrderByDescending(d => d.CreatedAt)
                .Take(Math.Clamp(request.RecentLimit, 0, 50))
                .ToListAsync(cancellationToken);

            var deployments = open.Concat(recent).ToList();
            var projectIds = deployments.Select(d => d.ProjectId).Distinct().ToList();
            var projects = await db.ProjectInfo
                .Where(p => projectIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var rows = deployments
                .Where(d => projects.ContainsKey(d.ProjectId))
                .Select(d => new { d, projects[d.ProjectId].ProjectName, projects[d.ProjectId].DockerProjectName });

            return rows
                .OrderByDescending(r => r.d.CreatedAt)
                .Select(r => new DeploymentTaskDto(DeploymentDto.From(r.d), r.ProjectName, r.DockerProjectName))
                .ToArray();
        }
    }
}
