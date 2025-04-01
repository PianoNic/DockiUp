using DockiUp.Application.Dtos;

namespace DockiUp.Application.Interfaces
{
    public interface INotificationService
    {
        Task SendNotification(string message);
        Task NotifyContainerUpdated(ContainerDto containerDto);
    }
}
