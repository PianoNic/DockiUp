using DockiUp.Application.Deployments;
using DockiUp.Application.ImageUpdates;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Toamaisutaa.Abstractions;

namespace DockiUp.API.Controllers
{
    /// <summary>Image updates: registry checks (#68), per-project policy (#69) and tag pinning (#70).</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ImageUpdatesController(IMediator mediator, ICurrentUser? currentUser = null) : ControllerBase
    {
        private string? Actor => currentUser?.IsAuthenticated == true ? currentUser.Name : null;

        /// <summary>The stored results of the last check, every project and service.</summary>
        [HttpGet(Name = "ListImageUpdates")]
        [ProducesResponseType(typeof(ImageUpdateDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<ImageUpdateDto[]>> ListImageUpdates()
            => Ok(await mediator.Send(new ListImageUpdatesQuery(), HttpContext.RequestAborted));

        /// <summary>Checks the registries now (one project, or all) and returns every stored result.</summary>
        [HttpPost("Check", Name = "CheckImageUpdates")]
        [ProducesResponseType(typeof(ImageUpdateDto[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ImageUpdateDto[]>> CheckImageUpdates([FromQuery] Guid? projectId)
            => Ok(await mediator.Send(new CheckImageUpdatesCommand(projectId), HttpContext.RequestAborted));

        [HttpGet("{projectId:guid}/Settings", Name = "GetImageUpdateSettings")]
        [ProducesResponseType(typeof(ImageUpdateSettingsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ImageUpdateSettingsDto>> GetImageUpdateSettings(Guid projectId)
            => Ok(await mediator.Send(new GetImageUpdateSettingsQuery(projectId), HttpContext.RequestAborted));

        /// <summary>Sets the policy (Off / Notify / Auto) and the services left out of checks.</summary>
        [HttpPut("{projectId:guid}/Settings", Name = "SetImageUpdateSettings")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> SetImageUpdateSettings(Guid projectId, [FromBody] SetImageUpdateSettingsRequest request)
        {
            await mediator.Send(new SetImageUpdateSettingsCommand(projectId, request.Policy, request.ExcludedServices ?? [], Actor), HttpContext.RequestAborted);
            return NoContent();
        }

        /// <summary>Tags of the image a service runs, newest first.</summary>
        [HttpGet("{projectId:guid}/Services/{serviceName}/Tags", Name = "ListServiceTags")]
        [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<string[]>> ListServiceTags(Guid projectId, string serviceName)
            => Ok(await mediator.Send(new ListServiceTagsQuery(projectId, serviceName), HttpContext.RequestAborted));

        /// <summary>Pins the service to a tag (dockiup.override.yml) and queues a deployment.</summary>
        [HttpPut("{projectId:guid}/Services/{serviceName}/Image", Name = "PinServiceImage")]
        [ProducesResponseType(typeof(DeploymentDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeploymentDto>> PinServiceImage(Guid projectId, string serviceName, [FromBody] PinServiceImageRequest request)
            => Accepted(await mediator.Send(new PinServiceImageCommand(projectId, serviceName, request.Tag, Actor), HttpContext.RequestAborted));

        /// <summary>Removes the service's pin (back to the compose file's image) and queues a deployment.</summary>
        [HttpDelete("{projectId:guid}/Services/{serviceName}/Image", Name = "ResetServiceImage")]
        [ProducesResponseType(typeof(DeploymentDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeploymentDto>> ResetServiceImage(Guid projectId, string serviceName)
            => Accepted(await mediator.Send(new PinServiceImageCommand(projectId, serviceName, null, Actor), HttpContext.RequestAborted));
    }
}
