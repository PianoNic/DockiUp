namespace DockiUp.Application.Interfaces
{
    public interface INotificationService
    {
        Task SendNotification(string message);
        Task NotifyContainerUpdated(string containerId, string status);
    }
}
