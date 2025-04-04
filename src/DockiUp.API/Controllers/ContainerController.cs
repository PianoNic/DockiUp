using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Queries;
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
        public async Task<ActionResult<ComposeInfoDto>> CreateContainer([FromBody] CreateContainerDto containerInformation)
        {
            var result = await _mediator.Send(new CreateContainerCommand(containerInformation));
            return Ok(result);
        }

        [HttpGet("GetContainers", Name = "GetContainers")]
        public async Task<ActionResult<ContainerDto[]>> GetContainerStatus()
        {
            var containers = await _mediator.Send(new GetContainersQuery());
            return Ok(containers);
        }

        [HttpPost("GetContainerStatus", Name = "GetContainerStatus")]
        public async Task<IActionResult> GetContainerStatus(ContainerDto container)
        {
            await _notificationService.NotifyContainerUpdated(container);
            return Ok(new { Message = "Notification sent successfully." });
        }
    }
}
