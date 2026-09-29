using DockiUp.Application.Dtos;
using DockiUp.Application.Enums;
using DockiUp.Application.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Queries
{
    /// <summary>At-a-glance dashboard counts: managed projects, container totals, recent activity.</summary>
    public sealed class GetDashboardStatsQuery : IRequest<DashboardStatsDto>
    {
    }

    public sealed class GetDashboardStatsQueryHandler : IRequestHandler<GetDashboardStatsQuery, DashboardStatsDto>
    {
        private readonly IDockerService _dockerService;
        private readonly IDockiUpDbContext _dbContext;
        private readonly IDockiUpDockerClient? _dockerClient;

        public GetDashboardStatsQueryHandler(IDockerService dockerService, IDockiUpDbContext dbContext, IDockiUpDockerClient? dockerClient = null)
        {
            _dockerService = dockerService;
            _dbContext = dbContext;
            _dockerClient = dockerClient;
        }

        public async ValueTask<DashboardStatsDto> Handle(GetDashboardStatsQuery request, CancellationToken cancellationToken)
        {
            var projects = await _dockerService.GetProjectsAsync();
            var containers = projects.SelectMany(p => p.Containers).ToList();
            var running = containers.Count(c => c.State == UpdateMethodType.Running);

            var totalProjects = await _dbContext.ProjectInfo.CountAsync(cancellationToken);

            var recentEntries = await _dbContext.ActivityEntries
                .OrderByDescending(e => e.CreatedAt)
                .Take(5)
                .ToListAsync(cancellationToken);

            var recentActivity = recentEntries.Select(e => new ActivityEntryDto
            {
                Id = e.Id,
                Action = e.Action,
                Target = e.Target,
                ProjectId = e.ProjectId,
                Details = e.Details,
                ActorName = e.ActorName,
                CreatedAt = e.CreatedAt,
            }).ToList();

            return new DashboardStatsDto
            {
                TotalProjects = totalProjects,
                TotalContainers = containers.Count,
                RunningContainers = running,
                RecentActivity = recentActivity,
                LocalDockerVersion = await TryGetLocalDockerVersionAsync(cancellationToken),
            };
        }

        // Best effort: the version is cosmetic, so an unreachable daemon must not fail the dashboard.
        private async Task<string?> TryGetLocalDockerVersionAsync(CancellationToken cancellationToken)
        {
            if (_dockerClient is null) return null;
            try { return (await _dockerClient.DockerClient.System.GetVersionAsync(cancellationToken)).Version; }
            catch { return null; }
        }
    }
}
