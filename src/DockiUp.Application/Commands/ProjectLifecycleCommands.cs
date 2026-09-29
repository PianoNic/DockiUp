using DockiUp.Application.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Commands
{
    public enum ProjectAction { Start, Stop, Restart }

    /// <summary>Start/stop/restart a compose project by DockiUp id, or by docker project name (+ node) for
    /// projects DockiUp didn't create. Uses `docker compose -p`, so no compose files or adoption needed.</summary>
    public sealed record ProjectLifecycleCommand(ProjectAction Action, Guid? ProjectId, string? DockerProjectName, Guid? NodeId = null) : IRequest;

    public sealed class ProjectLifecycleCommandHandler(IDockerServiceResolver dockerResolver, IDockiUpDbContext db, IActivityLogger activity)
        : IRequestHandler<ProjectLifecycleCommand>
    {
        public async ValueTask<Unit> Handle(ProjectLifecycleCommand request, CancellationToken cancellationToken)
        {
            var (name, nodeId, projectId) = await ProjectTarget.ResolveAsync(db, request.ProjectId, request.DockerProjectName, request.NodeId, cancellationToken);
            var docker = dockerResolver.Resolve(nodeId);
            await (request.Action switch
            {
                ProjectAction.Start => docker.StartProjectAsync(name),
                ProjectAction.Stop => docker.StopProjectAsync(name),
                _ => docker.RestartProjectAsync(name),
            });
            await activity.LogAsync(request.Action.ToString().ToLowerInvariant(), name, projectId, cancellationToken: cancellationToken);
            return default;
        }
    }

    /// <summary>Finds which compose project (and on which host) a request means: a DockiUp project by id or
    /// by its docker name, otherwise the given docker name on the node the caller says it runs on.</summary>
    internal static class ProjectTarget
    {
        public static async Task<(string Name, Guid? NodeId, Guid? ProjectId)> ResolveAsync(
            IDockiUpDbContext db, Guid? projectId, string? dockerProjectName, Guid? nodeId, CancellationToken cancellationToken)
        {
            if (projectId is { } id)
            {
                var project = await db.ProjectInfo.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                    ?? throw new KeyNotFoundException($"Project with id {id} not found.");
                return (project.DockerProjectName, project.NodeId, project.Id);
            }
            if (string.IsNullOrWhiteSpace(dockerProjectName))
                throw new ArgumentException("Provide either ProjectId or DockerProjectName.");

            var managed = await db.ProjectInfo.FirstOrDefaultAsync(p => p.DockerProjectName == dockerProjectName, cancellationToken);
            return managed is not null
                ? (managed.DockerProjectName, managed.NodeId, managed.Id)
                : (dockerProjectName, nodeId, null);
        }
    }

    /// <summary>Removes a compose project: `compose down` (volumes only if asked). For a DockiUp project
    /// also forgets it - its record, deployment history and the folder DockiUp created for it.</summary>
    public sealed record RemoveProjectCommand(Guid? ProjectId, string? DockerProjectName, Guid? NodeId = null, bool RemoveVolumes = false) : IRequest;

    public sealed class RemoveProjectCommandHandler(IDockerServiceResolver dockerResolver, IDockiUpDbContext db, IActivityLogger activity)
        : IRequestHandler<RemoveProjectCommand>
    {
        public async ValueTask<Unit> Handle(RemoveProjectCommand request, CancellationToken cancellationToken)
        {
            var (name, nodeId, projectId) = await ProjectTarget.ResolveAsync(db, request.ProjectId, request.DockerProjectName, request.NodeId, cancellationToken);
            var docker = dockerResolver.Resolve(nodeId);
            await docker.RemoveProjectAsync(name, request.RemoveVolumes);

            if (projectId is { } id && await db.ProjectInfo.FirstOrDefaultAsync(p => p.Id == id, cancellationToken) is { } project)
            {
                await docker.DeleteProjectFilesAsync(project.ProjectPath);
                db.Deployments.RemoveRange(db.Deployments.Where(d => d.ProjectId == id));
                db.ProjectInfo.Remove(project);
                await db.SaveChangesAsync(cancellationToken);
            }

            await activity.LogAsync("remove", name, projectId, request.RemoveVolumes ? "with volumes" : null, cancellationToken);
            return default;
        }
    }

    /// <summary>Downloads newer images for a DockiUp project (compose pull) without restarting it;
    /// the next deploy applies them. Returns compose's output.</summary>
    public sealed record PullProjectImagesCommand(Guid ProjectId) : IRequest<string>;

    public sealed class PullProjectImagesCommandHandler(IDockerServiceResolver dockerResolver, IDockiUpDbContext db, IActivityLogger activity)
        : IRequestHandler<PullProjectImagesCommand, string>
    {
        public async ValueTask<string> Handle(PullProjectImagesCommand request, CancellationToken cancellationToken)
        {
            var project = await db.ProjectInfo.FirstOrDefaultAsync(p => p.Id == request.ProjectId, cancellationToken)
                ?? throw new KeyNotFoundException($"Project with id {request.ProjectId} not found.");
            var output = new System.Text.StringBuilder();
            await dockerResolver.Resolve(project.NodeId).ComposePullAsync(
                new Dtos.ComposeTarget(project.ProjectPath, project.ComposePath, project.DockerProjectName),
                line => { output.AppendLine(line); return Task.CompletedTask; }, cancellationToken);
            await activity.LogAsync("pull", project.ProjectName, project.Id, cancellationToken: cancellationToken);
            return output.ToString();
        }
    }

    public sealed record RemoveContainerCommand(string ContainerId, Guid? NodeId = null) : IRequest;

    public sealed class RemoveContainerCommandHandler(IDockerServiceResolver dockerResolver, IActivityLogger activity)
        : IRequestHandler<RemoveContainerCommand>
    {
        public async ValueTask<Unit> Handle(RemoveContainerCommand request, CancellationToken cancellationToken)
        {
            await dockerResolver.Resolve(request.NodeId).RemoveContainerAsync(request.ContainerId);
            await activity.LogAsync("container.remove", request.ContainerId, cancellationToken: cancellationToken);
            return default;
        }
    }
}
