using System.Collections.Concurrent;
using System.Threading.Channels;
using DockiUp.Application.Deployments;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.API.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Deployments
{
    /// <summary>Every deploy - manual, webhook, periodic, create - goes through this one queue and runs
    /// one at a time, so two triggers can never race on the same checkout or compose project.</summary>
    // ponytail: global concurrency of 1 - per-project/per-node workers if deploy throughput ever matters.
    public sealed class DeploymentQueue(IServiceScopeFactory scopeFactory, ILogger<DeploymentQueue> logger)
        : BackgroundService, IDeploymentQueue
    {
        private readonly Channel<DeploymentRequest> _channel = Channel.CreateUnbounded<DeploymentRequest>();
        // Periodic checks waiting in the queue, so a slow deploy doesn't stack up checks for the same project.
        private readonly ConcurrentDictionary<Guid, byte> _pendingPeriodic = new();

        public bool TryEnqueue(DeploymentRequest request)
        {
            if (request.Trigger == DeploymentTrigger.Periodic && !_pendingPeriodic.TryAdd(request.ProjectId, 0))
                return false;
            return _channel.Writer.TryWrite(request);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RecoverAsync(stoppingToken);

            await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                if (request.Trigger == DeploymentTrigger.Periodic) _pendingPeriodic.TryRemove(request.ProjectId, out _);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<DeploymentRunner>().RunAsync(request, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Deployment for project {ProjectId} crashed", request.ProjectId);
                }
            }
        }

        // After a restart: a run that was mid-flight is lost (fail it), anything still queued runs again.
        private async Task RecoverAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDockiUpDbContext>();
            var open = await db.Deployments
                .Where(d => d.Status == DeploymentStatus.Queued || d.Status == DeploymentStatus.Running)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync(cancellationToken);
            foreach (var d in open.Where(d => d.Status == DeploymentStatus.Running))
            {
                d.Status = DeploymentStatus.Failed;
                d.Error = "Interrupted: DockiUp restarted while this deployment was running.";
                d.FinishedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            foreach (var d in open.Where(d => d.Status == DeploymentStatus.Queued))
                TryEnqueue(new DeploymentRequest(d.ProjectId, d.Trigger, d.Id, d.ActorName, d.TargetCommit));
        }
    }

    /// <summary>Deployment progress to every browser: status changes and live log lines.</summary>
    public sealed class DeploymentEvents(IHubContext<DockiUpHub> hub) : IDeploymentEvents
    {
        public Task ChangedAsync(DeploymentDto deployment) => hub.Clients.All.SendAsync("DeploymentChanged", deployment);

        public Task LogAsync(Guid deploymentId, string line) => hub.Clients.All.SendAsync("DeploymentLog", deploymentId, line);
    }
}
