using DockiUp.Application.Deployments;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.HostedServices
{
    /// <summary>Queues a periodic check for every project whose interval has elapsed. The check itself
    /// (git sync / image pull, redeploy only on change) runs in the deploy queue.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // timer loop; the work it queues is unit-tested
    public class PeriodicUpdateHostedService(IServiceScopeFactory scopeFactory, IDeploymentQueue queue, ILogger<PeriodicUpdateHostedService> logger)
        : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IDockiUpDbContext>();
                    var now = DateTime.UtcNow;
                    var projects = await db.ProjectInfo
                        .Where(p => p.ProjectUpdateMethod == ProjectUpdateMethod.Periodically && p.PeriodicIntervalInMinutes != null)
                        .Select(p => new { p.Id, p.PeriodicIntervalInMinutes, p.LastPeriodicUpdateAt })
                        .ToListAsync(stoppingToken);

                    foreach (var p in projects.Where(p => p.LastPeriodicUpdateAt is null
                                 || now - p.LastPeriodicUpdateAt.Value >= TimeSpan.FromMinutes(p.PeriodicIntervalInMinutes!.Value)))
                        queue.TryEnqueue(new DeploymentRequest(p.Id, DeploymentTrigger.Periodic));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Error in periodic update cycle");
                }
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }
}
