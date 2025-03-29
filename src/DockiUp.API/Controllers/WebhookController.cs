using DockiUp.API.Authorisation;
using DockiUp.Application.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly IMediator _mediator;
        public WebhookController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpPost("GitHub", Name = "GitHub")]
        [GitHubWebhookAuth("your_github_webhook_secret")]
        public async Task<IActionResult> GitHubWebhook(string identifier)
        {
            // Read the request body
            string payload;
            using (var reader = new System.IO.StreamReader(Request.Body))
            {
                payload = await reader.ReadToEndAsync();
            }

            // Process the webhook payload here
            Console.WriteLine(payload);

            return Ok();

        }

        [HttpPost("CreateWebhook", Name = "CreateWebhook")]
        public async Task<IActionResult<string>> CreateWebhook(string identifier)
        {
            var result = await _mediator.Send(new CreateWebhookCommand(identifier));

        }
    }
}