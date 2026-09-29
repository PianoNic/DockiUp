using DockiUp.Domain;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Queries
{
    public sealed class GetProjectsQuery : IRequest<ProjectDto[]>
    {
        public GetProjectsQuery()
        {
        }
    }

    public sealed class GetProjectsQueryHandler : IRequestHandler<GetProjectsQuery, ProjectDto[]>
    {
        private readonly IDockerService _localDocker;
        private readonly IDockerServiceResolver _dockerResolver;
        private readonly INodeDirectory _nodeDirectory;
        private readonly IDockiUpDbContext _dbContext;

        public GetProjectsQueryHandler(
            IDockerService localDocker,
            IDockerServiceResolver dockerResolver,
            INodeDirectory nodeDirectory,
            IDockiUpDbContext dbContext)
        {
            _localDocker = localDocker;
            _dockerResolver = dockerResolver;
            _nodeDirectory = nodeDirectory;
            _dbContext = dbContext;
        }

        public async ValueTask<ProjectDto[]> Handle(GetProjectsQuery request, CancellationToken cancellationToken)
        {
            // Docker project names the DB assigns to a node — these belong to that node's listing, not
            // the control-plane host's (and de-duplicates when a node happens to share the local daemon).
            var nodeOwned = (await _dbContext.ProjectInfo
                    .Where(p => p.NodeId != null)
                    .Select(p => p.DockerProjectName)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Local control-plane projects (already reconciled against the DB; NodeId stays null).
            var result = (await _localDocker.GetProjectsAsync())
                .Where(p => !nodeOwned.Contains(p.DockerProjectName))
                .ToList();

            // Fan out to each online node and merge its projects, reconciled against our records.
            foreach (var nodeId in _nodeDirectory.GetOnlineNodeIds())
            {
                ProjectDto[] nodeProjects;
                try
                {
                    nodeProjects = await _dockerResolver.Resolve(nodeId).GetRawProjectsAsync();
                }
                catch
                {
                    // A node that drops out mid-listing shouldn't fail the whole call.
                    continue;
                }

                // Last-wins on colliding docker names so the listing never throws.
                var dbProjects = (await _dbContext.ProjectInfo
                        .Where(p => p.NodeId == nodeId)
                        .ToListAsync(cancellationToken))
                    .GroupBy(p => p.DockerProjectName)
                    .ToDictionary(g => g.Key, g => g.Last());

                foreach (var project in nodeProjects)
                {
                    project.NodeId = nodeId;
                    if (dbProjects.TryGetValue(project.DockerProjectName, out var db))
                    {
                        project.Id = db.Id;
                        project.ProjectName = db.ProjectName;
                        project.ProjectDescription = db.Description ?? string.Empty;
                        project.ManagedByDockiUp = true;
                        project.ProjectPath = db.ProjectPath;
                        project.UpdateMethod = db.ProjectUpdateMethod.ToString();
                    }
                    result.Add(project);
                }
            }

            // DockiUp projects with no containers right now (first deploy queued or failed, or stopped with
            // `down`) must stay visible - that's where their deployment log lives. Offline nodes' projects too.
            var listed = result.Select(p => (p.NodeId, p.DockerProjectName.ToLowerInvariant())).ToHashSet();
            var missing = (await _dbContext.ProjectInfo.ToListAsync(cancellationToken))
                .Where(p => !listed.Contains((p.NodeId, p.DockerProjectName.ToLowerInvariant())));
            result.AddRange(missing.Select(p => new ProjectDto
            {
                Id = p.Id,
                ProjectName = p.ProjectName,
                ProjectDescription = p.Description ?? string.Empty,
                ManagedByDockiUp = true,
                DockerProjectName = p.DockerProjectName,
                Containers = [],
                ProjectPath = p.ProjectPath,
                UpdateMethod = p.ProjectUpdateMethod.ToString(),
                NodeId = p.NodeId,
            }));

            // What is deployed right now: the latest successful deployment of each DockiUp project.
            // ponytail: loads every successful row - a per-project "latest" subquery if history grows large.
            var ids = result.Where(p => p.Id is not null).Select(p => p.Id!.Value).ToList();
            var deployed = (await _dbContext.Deployments
                    .Where(d => ids.Contains(d.ProjectId) && d.Status == DeploymentStatus.Succeeded)
                    .ToListAsync(cancellationToken))
                .GroupBy(d => d.ProjectId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.FinishedAt).First());
            foreach (var p in result)
            {
                if (p.Id is not { } id || !deployed.TryGetValue(id, out var d)) continue;
                p.DeployedCommit = d.CommitAfter;
                p.DeployedCommitMessage = d.CommitMessage;
                p.DeployedAt = d.FinishedAt;
            }

            return result.ToArray();
        }
    }
}
