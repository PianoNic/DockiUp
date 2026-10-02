using DockiUp.Application.Commands;
using Mediator;

namespace DockiUp.API.HostedServices;

/// <summary>Every 30 seconds (and once at start), registers compose projects DockiUp doesn't know yet on the
/// local host and the online nodes (AutoAdoptCommand), so nothing has to be adopted by hand.</summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // timer loop; the command is unit-tested
public class AutoAdoptHostedService(IServiceScopeFactory scopeFactory, ILogger<AutoAdoptHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var adopted = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AutoAdoptCommand(), stoppingToken);
                if (adopted > 0)
                    logger.LogInformation("Adopted {Count} compose project(s) automatically", adopted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Automatic adopt failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
