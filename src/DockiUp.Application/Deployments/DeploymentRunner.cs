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
        IActivityLogger activity,
        ISecretsVaultService? vault = null,
        Git.IGitCredentialsProvider? gitCredentials = null,
        Mediator.IPublisher? publisher = null)
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
            var redact = new List<string>();
            // Defence in depth: whatever echoes a vault secret (compose, a build step), it never reaches the log.
            string Redact(string text) => redact.Aggregate(text, (t, secret) => t.Replace(secret, "***"));
            async Task Log(string line)
            {
                line = Redact(line);
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
                // Image updates (#69) recreate only the services whose image changed, without a git sync:
                // what's deployed from the repo stays as it is, so the record carries the deployed commit on.
                string[]? services = null;
                if (request.Trigger == DeploymentTrigger.ImageUpdate)
                {
                    services = await db.ImageUpdates
                        .Where(u => u.ProjectId == project.Id && u.UpdateAvailable)
                        .Select(u => u.ServiceName).ToArrayAsync(cancellationToken);
                    sync = await DeployedCommitAsync(project, cancellationToken);
                    if (services.Length == 0)
                    {
                        await Log("No image updates pending; nothing to do.");
                        deployment ??= Record(request, startedAt);
                        await CompleteAsync(deployment, project, sync, log, DeploymentStatus.Succeeded, null, cancellationToken);
                        return;
                    }
                    await Log("Updating images of: " + string.Join(", ", services));
                }
                else if (project.ProjectOrigin == ProjectOriginType.Git)
                {
                    var credentials = gitCredentials is null ? null : await gitCredentials.GetAsync(project.GitCredentialId, cancellationToken);
                    sync = await docker.SyncRepositoryAsync(project.ProjectPath, project.Branch, request.TargetCommit, Log, cancellationToken, credentials);
                    project.Branch ??= sync.Branch;
                    if (deployment is null && sync.Before == sync.After)
                    {
                        project.LastPeriodicUpdateAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                        return; // periodic, nothing new: no record, no restart
                    }
                }

                var secrets = await LoadSecretsAsync(project.Id, cancellationToken);
                redact.AddRange(secrets.Values.Where(v => v.Length >= 4));
                await docker.WriteEnvFileAsync(project.ProjectPath, project.ComposePath, secrets, cancellationToken);
                if (secrets.Count > 0) await Log($"Env file written with vault secrets: {string.Join(", ", secrets.Keys.Order(StringComparer.Ordinal))}");

                var up = await docker.ComposeUpAsync(
                    new ComposeTarget(project.ProjectPath, project.ComposePath, project.DockerProjectName, services), Log, cancellationToken);
                await ClearImageUpdatesAsync(project.Id, services, cancellationToken);

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
                var message = Redact(ex.Message);
                await Log("ERROR: " + message);
                deployment ??= Record(request, startedAt);
                await CompleteAsync(deployment, project, sync, log, DeploymentStatus.Failed, message, cancellationToken);
            }
        }

        // Mapped env name -> decrypted vault value. Names only ever go to the log, never values.
        private async Task<Dictionary<string, string>> LoadSecretsAsync(Guid projectId, CancellationToken cancellationToken)
        {
            var mappings = await db.ProjectSecrets.Where(m => m.ProjectId == projectId)
                .Join(db.Secrets, m => m.SecretId, s => s.Id, (m, s) => new { m.EnvName, s.Name })
                .ToListAsync(cancellationToken);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (mappings.Count == 0) return values;
            if (vault is null) throw new InvalidOperationException("The secrets vault is not available.");
            foreach (var m in mappings)
                values[m.EnvName] = await vault.RetrieveAsync(m.Name, cancellationToken)
                    ?? throw new InvalidOperationException($"Vault secret '{m.Name}' (for {m.EnvName}) no longer exists.");
            return values;
        }

        // The commit the project runs now (last successful deployment), for deployments that don't sync git.
        private async Task<GitSyncResult?> DeployedCommitAsync(ProjectInfo project, CancellationToken cancellationToken)
        {
            var last = await db.Deployments
                .Where(d => d.ProjectId == project.Id && d.Status == DeploymentStatus.Succeeded && d.CommitAfter != null)
                .OrderByDescending(d => d.FinishedAt)
                .FirstOrDefaultAsync(cancellationToken);
            return last is null ? null : new GitSyncResult(last.CommitAfter, last.CommitAfter, project.Branch ?? "", last.CommitMessage);
        }

        // The pulled images are now the registry's latest: the badge goes away without waiting for the next check.
        // Null services: a full deploy, which pulled every service.
        private async Task ClearImageUpdatesAsync(Guid projectId, string[]? services, CancellationToken cancellationToken)
        {
            var rows = await db.ImageUpdates.Where(u => u.ProjectId == projectId && (services == null || services.Contains(u.ServiceName)))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                row.UpdateAvailable = false;
                row.CurrentDigest = row.LatestDigest;
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

            // Notification channels (#75): the handler only queues, so this never slows or fails the deploy.
            if (publisher is not null)
                await publisher.Publish(new Notifications.DeploymentFinished(deployment.Id, project.Id, project.ProjectName,
                    status == DeploymentStatus.Succeeded, deployment.Trigger.ToString(), sync?.After, error), cancellationToken);
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
