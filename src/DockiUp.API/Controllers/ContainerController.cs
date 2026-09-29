using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Application.Queries;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContainerController : ControllerBase
    {
        private readonly IMediator _mediator;

        public ContainerController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet("GetContainer", Name = "GetContainer")]
        [ProducesResponseType(typeof(ContainerDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ContainerDto>> GetContainer([FromQuery] string containerId, [FromQuery] Guid? nodeId = null)
        {
            try
            {
                var container = await _mediator.Send(new GetContainerQuery(containerId, nodeId), HttpContext.RequestAborted);
                return Ok(container);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("StartContainer", Name = "StartContainer")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> StartContainer([FromQuery] string containerId, [FromQuery] Guid? nodeId = null)
        {
            await _mediator.Send(new StartContainerCommand(containerId, nodeId), HttpContext.RequestAborted);
            return NoContent();
        }

        [HttpPost("StopContainer", Name = "StopContainer")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> StopContainer([FromQuery] string containerId, [FromQuery] Guid? nodeId = null)
        {
            await _mediator.Send(new StopContainerCommand(containerId, nodeId), HttpContext.RequestAborted);
            return NoContent();
        }

        [HttpPost("RemoveContainer", Name = "RemoveContainer")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> RemoveContainer([FromQuery] string containerId, [FromQuery] Guid? nodeId = null)
        {
            await _mediator.Send(new RemoveContainerCommand(containerId, nodeId), HttpContext.RequestAborted);
            return NoContent();
        }

        [HttpPost("RestartContainer", Name = "RestartContainer")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> RestartContainer([FromQuery] string containerId, [FromQuery] Guid? nodeId = null)
        {
            await _mediator.Send(new RestartContainerCommand(containerId, nodeId), HttpContext.RequestAborted);
            return NoContent();
        }

        [HttpGet("GetContainerLogs", Name = "GetContainerLogs")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<string>> GetContainerLogs([FromQuery] string containerId, [FromQuery] int? tail = 200, [FromQuery] Guid? nodeId = null)
        {
            try
            {
                var logs = await _mediator.Send(new GetContainerLogsQuery(containerId, tail, nodeId), HttpContext.RequestAborted);
                return Ok(logs);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
    }
}
