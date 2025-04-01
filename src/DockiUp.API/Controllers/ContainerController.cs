using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContainerController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly INotificationService _notificationService;

        public ContainerController(IMediator mediator, INotificationService notificationService)
        {
            _mediator = mediator;
            _notificationService = notificationService;
        }

        [HttpPost("CreateContainer", Name = "CreateContainer")]
        public async Task<IActionResult> CreateContainer([FromBody] CreateContainerDto containerInformation)
        {
            await _mediator.Send(new CreateContainerCommand(containerInformation));
            return Created();
        }

        [HttpGet("GetContainerStatus", Name = "GetContainerStatus")]
        public async Task<IActionResult> GetContainerStatus(string containerId, string containerName)
        {
            await _notificationService.NotifyContainerUpdated(containerId, containerName);
            return Ok(new { Message = "Notification sent successfully." });
        }
    }
}
