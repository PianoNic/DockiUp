using DockiUp.Application.Commands;
using DockiUp.Application.Models;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DockiUp.API.Controllers
{
    /// <summary>Webhook endpoint for git providers (Komodo-style: trigger update on push).</summary>
    // Anonymous: git providers can't carry a user token; the webhook secret is the credential here.
    [AllowAnonymous]
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly IOptions<DockiUpWebhookOptions> _webhookOptions;

        public WebhookController(IMediator mediator, IOptions<DockiUp.Application.Models.DockiUpWebhookOptions> webhookOptions)
        {
            _mediator = mediator;
            _webhookOptions = webhookOptions;
        }

        /// <summary>Trigger project update by id. Use header X-Webhook-Secret or query secret to match configured WebhookSecret.</summary>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> Trigger([FromQuery] Guid projectId, [FromHeader(Name = "X-Webhook-Secret")] string? secretHeader, [FromQuery] string? secret)
        {
            var configured = _webhookOptions.Value.WebhookSecret;
            if (!string.IsNullOrEmpty(configured))
            {
                var provided = secretHeader ?? secret;
                if (string.IsNullOrEmpty(provided) || provided != configured)
                    return BadRequest("Invalid or missing webhook secret.");
            }

            await _mediator.Send(new UpdateProjectCommand(projectId), HttpContext.RequestAborted);
            return NoContent();
        }
    }
}
