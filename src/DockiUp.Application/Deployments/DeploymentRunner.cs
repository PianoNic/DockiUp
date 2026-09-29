using System.Text;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Deployments
{
    /// <summary>Runs one deployment: git sync (git projects), then compose pull + up, on whichever host the
    /// project lives. Called only by the deploy queue, which runs one at a time, so runs never overlap.</summary>
    public sealed class DeploymentRunner(
        IDockiUpDbContext db,
        IDockerServiceResolver dockerResolver,
        IDeploymentEvents events,
        IActivityLogger activity)
    {
        public async Task RunAsync(DeploymentRequest request, CancellationToken cancellationToken)
        {
            var project = await db.ProjectInfo.FirstOrDefaultAsync(p => p.Id == request.ProjectId, cancellationToken);
            var deployment = request.DeploymentId is { } id
                ? await db.Deployments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
                : null;
            if (project is null)
            {
                if (deployment is not null) await FinishAsync(deployment, DeploymentStatus.Failed, "The project no longer exists.", cancellationToken);
                return;
            }

            // Periodic checks run quietly and only leave a record when they did something (or failed);
            // everything else was recorded as Queued up front and streams live.
            var log = new StringBuilder();
            async Task Log(string line)
            {
                log.AppendLine(line);
                if (deployment is not null) await events.LogAsync(deployment.Id, line);
            }

            if (deployment is not null)
            {
                deployment.Status = DeploymentStatus.Running;
                deployment.StartedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await events.ChangedAsync(DeploymentDto.From(deployment));
            }

            var startedAt = DateTime.UtcNow;
            var docker = dockerResolver.Resolve(project.NodeId);
            GitSyncResult? sync = null;
            try
            {
                if (project.ProjectOrigin == ProjectOriginType.Git)
                {
                    sync = await docker.SyncRepositoryAsync(project.ProjectPath, project.Branch, request.TargetCommit, Log, cancellationToken);
                    project.Branch ??= sync.Branch;
                    if (deployment is null && sync.Before == sync.After)
                    {
                        project.LastPeriodicUpdateAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                        return; // periodic, nothing new: no record, no restart
                    }
                }

                var up = await docker.ComposeUpAsync(
                    new ComposeTarget(project.ProjectPath, project.ComposePath, project.DockerProjectName), Log, cancellationToken);

                if (deployment is null && !up.Changed && sync is null)
                {
                    project.LastPeriodicUpdateAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    return; // periodic image check found nothing newer
                }

                await Log(up.Changed ? "Containers updated." : "Everything was already up to date.");
                deployment ??= Record(request, startedAt);
                await CompleteAsync(deployment, project, sync, log, DeploymentStatus.Succeeded, null, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await Log("ERROR: " + ex.Message);
                deployment ??= Record(request, startedAt);
                await CompleteAsync(deployment, project, sync, log, DeploymentStatus.Failed, ex.Message, cancellationToken);
            }
        }

        private Deployment Record(DeploymentRequest request, DateTime startedAt)
        {
            var deployment = new Deployment { ProjectId = request.ProjectId, Trigger = request.Trigger, ActorName = request.ActorName, StartedAt = startedAt };
            db.Deployments.Add(deployment);
            return deployment;
        }

        private async Task CompleteAsync(Deployment deployment, ProjectInfo project, GitSyncResult? sync, StringBuilder log,
            DeploymentStatus status, string? error, CancellationToken cancellationToken)
        {
            deployment.CommitBefore = sync?.Before;
            deployment.CommitAfter = sync?.After;
            deployment.CommitMessage = sync?.Message;
            deployment.Log = log.ToString();
            if (deployment.Trigger == DeploymentTrigger.Periodic) project.LastPeriodicUpdateAt = DateTime.UtcNow;
            await FinishAsync(deployment, status, error, cancellationToken);

            var commit = sync?.After is { } sha ? $" @ {sha[..Math.Min(7, sha.Length)]}" : "";
            await activity.LogAsync(status == DeploymentStatus.Succeeded ? "deploy" : "deploy.failed", project.ProjectName, project.Id,
                $"{deployment.Trigger}{commit}", cancellationToken, deployment.ActorName);
        }

        private async Task FinishAsync(Deployment deployment, DeploymentStatus status, string? error, CancellationToken cancellationToken)
        {
            deployment.Status = status;
            deployment.Error = error;
            deployment.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await events.ChangedAsync(DeploymentDto.From(deployment));
        }
    }
}
