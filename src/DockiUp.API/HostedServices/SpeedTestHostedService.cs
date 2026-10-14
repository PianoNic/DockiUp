using DockiUp.API.Controllers;
using DockiUp.Application;
using DockiUp.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.HostedServices;

/// <summary>Runs the internet speed test on each online host every SpeedTest:Interval (default 1d; 0 turns it off).
/// The last run comes from the stored results, so a restart doesn't test every host again. One test at a time.</summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // timer loop; the test itself runs on the hosts
public class SpeedTestHostedService(IServiceScopeFactory scopeFactory, INodeDirectory nodes, IConfiguration configuration, ILogger<SpeedTestHostedService> logger)
    : BackgroundService
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); // after startup's busy minute, with nodes connected
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            var interval = Durations.Parse(configuration["SpeedTest:Interval"]) ?? DefaultInterval;
            if (interval <= TimeSpan.Zero) continue;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDockiUpDbContext>();
                var last = (await db.SpeedTestResults.GroupBy(r => r.NodeId).Select(g => new { NodeId = g.Key, At = g.Max(r => r.At) }).ToListAsync(stoppingToken))
                    .ToDictionary(r => r.NodeId?.ToString() ?? "local", r => r.At);
                foreach (var nodeId in new Guid?[] { null }.Concat(nodes.GetOnlineNodeIds().Select(id => (Guid?)id)))
                {
                    if (last.TryGetValue(nodeId?.ToString() ?? "local", out var at) && DateTime.UtcNow - at < interval) continue;
                    await HostsController.RunSpeedTestAsync(scope.ServiceProvider, nodeId, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Scheduled speed test failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
