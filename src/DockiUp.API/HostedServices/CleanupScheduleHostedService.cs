using DockiUp.Application.Interfaces;
using DockiUp.Application.Monitoring;
using DockiUp.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.HostedServices;

/// <summary>Once a minute, runs the cleanup of every host whose schedule is due (CleanupScheduleRules.IsDue).
/// An offline node is skipped and picked up again once it is back, as long as the slot hasn't been run.</summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // timer loop; the due rule and the run command are unit-tested
public class CleanupScheduleHostedService(IServiceScopeFactory scopeFactory, INodeDirectory nodes, ILogger<CleanupScheduleHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDockiUpDbContext>();
                var now = DateTime.UtcNow;
                var online = nodes.GetOnlineNodeIds();
                var due = (await db.CleanupSchedules.Where(s => s.Frequency != CleanupFrequency.Off).ToListAsync(stoppingToken))
                    .Where(s => CleanupScheduleRules.IsDue(s, now) && (s.NodeId is null || online.Contains(s.NodeId.Value)))
                    .Select(s => s.NodeId)
                    .ToList();

                foreach (var nodeId in due)
                {
                    using var runScope = scopeFactory.CreateScope();
                    try
                    {
                        await runScope.ServiceProvider.GetRequiredService<IMediator>().Send(new RunCleanupCommand(nodeId, Scheduled: true), stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Scheduled cleanup failed on {Host}", nodeId?.ToString() ?? "local host");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in cleanup schedule cycle");
            }
        }
    }
}
