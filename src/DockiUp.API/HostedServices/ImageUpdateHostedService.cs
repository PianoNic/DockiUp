using DockiUp.API.SignalR;
using DockiUp.Application;
using DockiUp.Application.ImageUpdates;
using DockiUp.Application.Interfaces;
using DockiUp.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.HostedServices
{
    /// <summary>Checks each project's images on its own interval, else DockiUp's (ImageUpdates:Interval, see
    /// <see cref="ImageUpdateSchedule"/>). Looks every 5 seconds, so a changed interval applies right away. The first
    /// checks run a minute after startup, once nodes have reconnected.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // timer loop; the schedule and the check are unit-tested
    public class ImageUpdateHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ImageUpdateHostedService> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var lastRun = new Dictionary<Guid, DateTime>();
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            using var timer = new PeriodicTimer(Durations.Minimum);
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var projects = await scope.ServiceProvider.GetRequiredService<IDockiUpDbContext>().ProjectInfo
                        .Where(p => p.ImageUpdatePolicy != ImageUpdatePolicy.Off)
                        .Select(p => new { p.Id, p.ImageUpdateIntervalSeconds })
                        .ToListAsync(stoppingToken);
                    var global = ImageUpdateSchedule.GlobalInterval(configuration["ImageUpdates:Interval"]);
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                    foreach (var id in ImageUpdateSchedule.Due(projects.Select(p => (p.Id, p.ImageUpdateIntervalSeconds)), global, lastRun, DateTime.UtcNow))
                    {
                        lastRun[id] = DateTime.UtcNow;
                        try { await mediator.Send(new CheckImageUpdatesCommand(id), stoppingToken); }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            logger.LogError(ex, "Image update check of project {ProjectId} failed", id);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Image update schedule failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }

    /// <summary>Image update results to every browser.</summary>
    public sealed class ImageUpdateEvents(IHubContext<DockiUpHub> hub) : IImageUpdateEvents
    {
        public Task ChangedAsync(ImageUpdateDto[] updates) => hub.Clients.All.SendAsync("ImageUpdatesChanged", updates);
    }
}
