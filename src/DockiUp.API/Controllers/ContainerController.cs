using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Queries;
using DockiUp.Domain.Enums;
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

        [HttpPost("PrepareWorkingDirectory", Name = "PrepareWorkingDirectory")]
        public async Task<ActionResult> PrepareWorkingDirectory([FromBody] WorkingDirectorySetupDto workingDirectorySetupDto)
        {
            await _mediator.Send(new PrepareWorkingDirectoryCommand(workingDirectorySetupDto));
            return Created();
        }

        [HttpGet("GetEnvironmentFiles", Name = "GetEnvironmentFiles")]
        public async Task<ActionResult<List<ComposeDto>>> GetEnvironmentFiles(int containerDbId)
        {
            var result = await _mediator.Send(new GetEnvironmentFilesQuery(containerDbId));
            return Ok(result);
        }

        [HttpPost("SetEnvironmentFiles", Name = "SetEnvironmentFiles")]
        public async Task<ActionResult> SetEnvironmentFiles(ComposeDto composeDto, int containerDbId)
        {
            await _mediator.Send(new SetEnvironmentFilesCommand(containerDbId, composeDto));
            return Created();
        }

        [HttpPost("SetUpdateMethod", Name = "SetUpdateMethod")]
        public async Task<ActionResult> SetUpdateMethod([FromBody] UpdateMethod updateMethod, int containerId)
        {
            await _mediator.Send(new SetUpdateMethodCommand(updateMethod, containerId));
            return Created();
        }

        [HttpGet("GetContainers", Name = "GetContainers")]
        public async Task<ActionResult<ContainerDto[]>> GetContainers()
        {
            var containers = await _mediator.Send(new GetContainersQuery());
            return Ok(containers);
        }

        //[HttpPost("GetContainerStatus", Name = "GetContainerStatus")]
        //public async Task<IActionResult> GetContainerStatus(ComposeInfoDto container)
        //{
        //    await _notificationService.NotifyContainerUpdated(container);
        //    return Ok(new { Message = "Notification sent successfully." });
        //}
    }
}
