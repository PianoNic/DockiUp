using DockiUp.Application.Dtos;
using DockiUp.Application.Monitoring;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    /// <summary>Live and historical CPU / memory / network of containers (local host and nodes).</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class StatsController(IMediator mediator) : ControllerBase
    {
        /// <summary>Latest sample per running container; narrow with a compose project or container (and its node).</summary>
        [HttpGet("GetContainerStats", Name = "GetContainerStats")]
        [ProducesResponseType(typeof(ContainerStatsDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<ContainerStatsDto[]>> GetContainerStats([FromQuery] string? projectName = null, [FromQuery] string? containerId = null, [FromQuery] Guid? nodeId = null)
            => Ok(await mediator.Send(new GetContainerStatsQuery(projectName, containerId, nodeId), HttpContext.RequestAborted));

        /// <summary>1-minute points of one container (by name, stable across redeploys) over the last 1-24 hours.</summary>
        [HttpGet("GetContainerStatsHistory", Name = "GetContainerStatsHistory")]
        [ProducesResponseType(typeof(ContainerStatsPointDto[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ContainerStatsPointDto[]>> GetContainerStatsHistory([FromQuery] string containerName, [FromQuery] Guid? nodeId = null, [FromQuery] int hours = 24)
            => Ok(await mediator.Send(new GetContainerStatsHistoryQuery(containerName, nodeId, hours), HttpContext.RequestAborted));
    }
}
