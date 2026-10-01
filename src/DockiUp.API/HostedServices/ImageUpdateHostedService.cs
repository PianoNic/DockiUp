using DockiUp.API.SignalR;
using DockiUp.Application.ImageUpdates;
using Mediator;
using Microsoft.AspNetCore.SignalR;

namespace DockiUp.API.HostedServices
{
    /// <summary>Runs the image update check for every project on a timer: `ImageUpdates:IntervalHours`
    /// (default 6; 0 disables the timer, "Check now" still works). The first run is shortly after startup.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // timer loop; the check it runs is unit-tested
    public class ImageUpdateHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ImageUpdateHostedService> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var hours = configuration.GetValue("ImageUpdates:IntervalHours", 6.0);
            if (hours <= 0) return;
            var interval = TimeSpan.FromHours(hours);

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // let nodes reconnect first
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CheckImageUpdatesCommand(), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Image update check failed");
                }
                await Task.Delay(interval, stoppingToken);
            }
        }
    }

    /// <summary>Image update results to every browser.</summary>
    public sealed class ImageUpdateEvents(IHubContext<DockiUpHub> hub) : IImageUpdateEvents
    {
        public Task ChangedAsync(ImageUpdateDto[] updates) => hub.Clients.All.SendAsync("ImageUpdatesChanged", updates);
    }
}
