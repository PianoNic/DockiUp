using DockiUp.Application.Dtos;
using DockiUp.Application.Monitoring;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    /// <summary>Images, volumes and networks per host (nodeId null = the control-plane host): listing, delete,
    /// prune, and the scheduled cleanup.</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ResourcesController(IMediator mediator) : ControllerBase
    {
        [HttpGet("GetResources", Name = "GetResources")]
        [ProducesResponseType(typeof(DockerResourcesDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<DockerResourcesDto>> GetResources([FromQuery] Guid? nodeId = null)
            => Ok(await mediator.Send(new GetResourcesQuery(nodeId), HttpContext.RequestAborted));

        /// <summary>Deletes one image (id), volume (name) or network (id). 400 with the daemon's reason when it is in use.</summary>
        [HttpPost("RemoveResource", Name = "RemoveResource")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> RemoveResource([FromQuery] ResourceKind kind, [FromQuery] string id, [FromQuery] Guid? nodeId = null, [FromQuery] string? name = null)
        {
            await mediator.Send(new RemoveResourceCommand(kind, id, nodeId, name), HttpContext.RequestAborted);
            return NoContent();
        }

        [HttpPost("PruneResources", Name = "PruneResources")]
        [ProducesResponseType(typeof(PruneResultDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<PruneResultDto>> PruneResources([FromBody] PruneRequest request, [FromQuery] Guid? nodeId = null)
            => Ok(await mediator.Send(new PruneResourcesCommand(request, nodeId), HttpContext.RequestAborted));

        [HttpGet("GetCleanupSchedule", Name = "GetCleanupSchedule")]
        [ProducesResponseType(typeof(CleanupScheduleDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<CleanupScheduleDto>> GetCleanupSchedule([FromQuery] Guid? nodeId = null)
            => Ok(await mediator.Send(new GetCleanupScheduleQuery(nodeId), HttpContext.RequestAborted));

        [HttpPut("SaveCleanupSchedule", Name = "SaveCleanupSchedule")]
        [ProducesResponseType(typeof(CleanupScheduleDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<CleanupScheduleDto>> SaveCleanupSchedule([FromBody] SaveCleanupScheduleCommand request)
            => Ok(await mediator.Send(request, HttpContext.RequestAborted));

        /// <summary>Runs the host's cleanup now with the scheduled keep-rule (see CleanupScheduleRules.ScheduledRequest).</summary>
        [HttpPost("RunCleanup", Name = "RunCleanup")]
        [ProducesResponseType(typeof(PruneResultDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<PruneResultDto>> RunCleanup([FromQuery] Guid? nodeId = null)
            => Ok(await mediator.Send(new RunCleanupCommand(nodeId), HttpContext.RequestAborted));
    }
}
