using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using MediatR;
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

        [HttpPost("CreateContainer", Name = "CreateContainer")]
        public async Task<IActionResult> CreateContainer([FromBody] CreateContainerDto containerInformation)
        {
            await _mediator.Send(new CreateContainerCommand(containerInformation));
            return Created();
        }

        [HttpGet("GetContainerStatus", Name = "GetContainerStatus")]
        public async Task<IActionResult> GetContainerStatus([FromQuery] string containerName)
        {

        }
    }
}
