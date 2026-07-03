using DockiUp.Application.Commands;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DockiUp.API.HostedServices
{
    /// <summary>Runs periodic project updates (Komodo-style resource poll interval).</summary>
    // Timer-driven background loop; exercised by the running app rather than unit-mocked.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PeriodicUpdateHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PeriodicUpdateHostedService> _logger;
        private readonly TimeSpan _pollInterval;

        public PeriodicUpdateHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<PeriodicUpdateHostedService> logger,
            IOptions<PeriodicUpdateOptions> options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollInterval = options.Value.PollInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<IDockiUpDbContext>();
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                    var projects = dbContext.ProjectInfo
                        .Where(p => p.ProjectUpdateMethod == ProjectUpdateMethod.Periodically && p.PeriodicIntervalInMinutes != null)
                        .ToList();

                    foreach (var project in projects)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        var interval = TimeSpan.FromMinutes(project.PeriodicIntervalInMinutes!.Value);
                        var due = project.LastPeriodicUpdateAt == null ||
                                  DateTime.UtcNow - project.LastPeriodicUpdateAt.Value >= interval;
                        if (!due) continue;

                        try
                        {
                            await mediator.Send(new UpdateProjectCommand(project.Id), stoppingToken);
                            project.LastPeriodicUpdateAt = DateTime.UtcNow;
                            await dbContext.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("Periodic update completed for project {ProjectName} (id {Id})", project.ProjectName, project.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Periodic update failed for project {ProjectName} (id {Id})", project.ProjectName, project.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in periodic update cycle");
                }

                await Task.Delay(_pollInterval, stoppingToken);
            }
        }
    }

    public class PeriodicUpdateOptions
    {
        /// <summary>How often to check for due projects. Default 1 minute.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
