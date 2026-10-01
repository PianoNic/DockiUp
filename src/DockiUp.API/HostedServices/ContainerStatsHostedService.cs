using DockiUp.API.SignalR;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Monitoring;
using Microsoft.AspNetCore.SignalR;

namespace DockiUp.API.HostedServices;

/// <summary>Samples every running container on the control-plane host and on each online node at a fixed
/// interval (Stats:IntervalSeconds, default 15), keeps the live values in <see cref="ContainerStatsStore"/>,
/// persists 1-minute averages (kept Stats:RetentionHours, default 24) and pushes the live values to browsers.</summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // timer loop over live daemons; the store/downsampler it drives are unit-tested
public class ContainerStatsHostedService(
    IServiceScopeFactory scopeFactory,
    ContainerStatsStore store,
    INodeDirectory nodes,
    IHubContext<DockiUpHub> hub,
    IConfiguration configuration,
    ILogger<ContainerStatsHostedService> logger) : BackgroundService
{
    public const string ContainerStatsMessage = "ContainerStats";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Stats:IntervalSeconds", 15)));
        var retention = TimeSpan.FromHours(Math.Max(1, configuration.GetValue("Stats:RetentionHours", 24)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await SampleAsync(retention, interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Container stats cycle failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SampleAsync(TimeSpan retention, TimeSpan interval, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDockerServiceResolver>();
        Guid?[] hosts = [null, .. nodes.GetOnlineNodeIds().Select(id => (Guid?)id)];

        // Hosts that went offline since the last round: drop their stale live values.
        foreach (var gone in store.Latest.Select(s => s.NodeId).Distinct().Where(id => !hosts.Contains(id)))
            store.Forget(gone);

        var rounds = await Task.WhenAll(hosts.Select(async nodeId =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(interval * 2);
            try
            {
                return (nodeId, Stats: await resolver.Resolve(nodeId).GetContainerStatsAsync(timeout.Token));
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Could not sample container stats on {Host}: {Message}", nodeId?.ToString() ?? "local host", ex.Message);
                return (nodeId, Stats: (ContainerStatsDto[]?)null);
            }
        }));

        var points = new List<Domain.ContainerStatSample>();
        foreach (var (nodeId, stats) in rounds)
        {
            if (stats is null) store.Forget(nodeId);
            else points.AddRange(store.Ingest(nodeId, stats));
        }

        if (points.Count > 0)
        {
            var db = scope.ServiceProvider.GetRequiredService<IDockiUpDbContext>();
            await ContainerStatsHistory.PersistAsync(db, points, DateTime.UtcNow, retention, stoppingToken);
        }

        await hub.Clients.All.SendAsync(ContainerStatsMessage, store.Latest, stoppingToken);
    }
}
