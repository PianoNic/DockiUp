using DockiUp.API.SignalR;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;

namespace DockiUp.API.HostedServices;

/// <summary>Periodically fetches current container/project state and broadcasts via SignalR only when something changed (crashes, new deployments, state changes).</summary>
// Background polling loop over a live daemon + hub; exercised by the running app, not unit-mocked.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class ContainerStateBroadcastHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContainerStateBroadcastHostedService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Snapshot of last known state for change detection. Format: "projectName|containerId:state|..." per project.</summary>
    private string? _lastStateSnapshot;

    public ContainerStateBroadcastHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ContainerStateBroadcastHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                using var scope = _scopeFactory.CreateScope();
                var docker = scope.ServiceProvider.GetRequiredService<IDockerService>();
                var projects = await docker.GetProjectsAsync();

                var snapshot = BuildStateSnapshot(projects);
                if (snapshot != _lastStateSnapshot)
                {
                    _lastStateSnapshot = snapshot;
                    var broadcast = scope.ServiceProvider.GetRequiredService<IDockiUpHubBroadcastService>();
                    await broadcast.BroadcastContainersChangedAsync(projects, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Container state broadcast cycle failed");
            }
        }
    }

    private static string BuildStateSnapshot(ProjectDto[] projects)
    {
        if (projects == null || projects.Length == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var p in projects.OrderBy(x => x.DockerProjectName))
        {
            var containerPart = string.Join("|",
                (p.Containers ?? Array.Empty<ContainerDto>())
                .OrderBy(c => c.Id)
                .Select(c => $"{c.Id}:{c.State}"));
            parts.Add($"{p.DockerProjectName}=[{containerPart}]");
        }
        return string.Join(";", parts);
    }
}
