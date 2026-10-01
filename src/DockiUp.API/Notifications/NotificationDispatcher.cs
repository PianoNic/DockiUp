using System.Threading.Channels;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Notifications
{
    /// <summary>Sends queued notifications to every enabled channel subscribed to the event, in the background:
    /// a slow or broken channel never holds up a deploy or a request. Each send has a timeout; failures are
    /// logged (without the channel's credential) and dropped.</summary>
    public sealed class NotificationDispatcher(IServiceScopeFactory scopeFactory, INotificationSender sender, ILogger<NotificationDispatcher> logger)
        : BackgroundService, INotificationDispatcher
    {
        public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

        // Bounded: if every channel is down and events keep coming, drop the oldest rather than grow forever.
        private readonly Channel<NotificationMessage> _queue = Channel.CreateBounded<NotificationMessage>(
            new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

        public void Enqueue(NotificationMessage message) => _queue.Writer.TryWrite(message);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try { await DispatchAsync(message, stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Could not dispatch {Event} notification: {Message}", message.Event, ex.Message);
                }
            }
        }

        public async Task DispatchAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDockiUpDbContext>();
            var vault = scope.ServiceProvider.GetRequiredService<ISecretsVaultService>();

            var channels = await db.NotificationChannels.AsNoTracking().Where(c => c.Enabled).ToListAsync(cancellationToken);
            foreach (var channel in channels.Where(c => c.Events.HasFlag(message.Event)))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(SendTimeout);
                try
                {
                    await sender.SendAsync(NotificationChannels.ToTarget(channel, vault), message, timeout.Token);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    var reason = ex is OperationCanceledException ? $"timed out after {SendTimeout.TotalSeconds:0}s" : ex.Message;
                    logger.LogWarning("Notification {Event} to channel {Channel} ({Type}) failed: {Reason}", message.Event, channel.Name, channel.Type, reason);
                }
            }
        }
    }
}
