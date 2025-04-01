using DockiUp.Application.Interfaces;
using DockiUp.Application.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace DockiUp.Application.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task SendNotification(string message)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", message);
        }

        public async Task NotifyContainerUpdated(string containerId, string status)
        {
            await _hubContext.Clients.All.SendAsync("ContainerUpdated", new { containerId, status });
        }
    }
}
