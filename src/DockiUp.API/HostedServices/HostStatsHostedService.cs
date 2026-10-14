using DockiUp.API.SignalR;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Monitoring;
using Microsoft.AspNetCore.SignalR;

namespace DockiUp.API.HostedServices;

/// <summary>Reads every host's own resources (this one and each online node) on the container stats interval
/// (Stats:IntervalSeconds), keeps the latest in <see cref="HostStatsStore"/>, saves minute averages for the history
/// and pushes the readings to browsers as "HostStats".</summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // timer loop over live hosts; the store and the parsers are unit-tested
public class HostStatsHostedService(
    IServiceScopeFactory scopeFactory,
    HostStatsStore store,
    INodeDirectory nodes,
    IHubContext<DockiUpHub> hub,
    IConfiguration configuration,
    ILogger<HostStatsHostedService> logger) : BackgroundService
{
    public const string HostStatsMessage = "HostStats";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            var interval = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Stats:IntervalSeconds", 15.0)));
            timer.Period = interval;
            try
            {
                await SampleAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Host stats cycle failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SampleAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDockerServiceResolver>();
        Guid?[] hosts = [null, .. nodes.GetOnlineNodeIds().Select(id => (Guid?)id)];

        foreach (var gone in store.Latest.Select(s => s.NodeId).Where(id => !hosts.Contains(id)).ToList())
            store.Forget(gone);

        var rounds = await Task.WhenAll(hosts.Select(async nodeId =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(interval * 2);
            try
            {
                return (nodeId, Stats: await resolver.Resolve(nodeId).GetHostStatsAsync(timeout.Token));
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // A node on an older DockiUp doesn't know GetHostStats yet: it simply shows no stats.
                logger.LogDebug("Could not read host stats of {Host}: {Message}", nodeId?.ToString() ?? "local host", ex.Message);
                return (nodeId, Stats: (Application.Dtos.HostStatsDto?)null);
            }
        }));

        var db = scope.ServiceProvider.GetRequiredService<IDockiUpDbContext>();
        foreach (var (nodeId, stats) in rounds)
        {
            if (stats is null) { store.Forget(nodeId); continue; }
            if (store.Ingest(nodeId, stats) is { } minute)
                await HostStatsHistory.PersistAsync(db, minute, stoppingToken);
        }

        await hub.Clients.All.SendAsync(HostStatsMessage, store.Latest, stoppingToken);
    }
}
