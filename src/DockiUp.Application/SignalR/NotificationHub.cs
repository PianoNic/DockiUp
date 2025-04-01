using Microsoft.AspNetCore.SignalR;

namespace DockiUp.Application.SignalR
{
    public class NotificationHub : Hub
    {
        public async Task SendMessage(string message)
        {
            await Clients.All.SendAsync("ContainerUpdated", message);
        }

        public async Task NotifyContainerUpdated(string containerId, string status)
        {
            await Clients.All.SendAsync("ContainerUpdated", new { containerId, status });
        }
    }
}
