using DockiUp.API.Authorisation;
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
            string payload;
            using (var reader = new System.IO.StreamReader(Request.Body))
            {
                payload = await reader.ReadToEndAsync();
            }

            Console.WriteLine(payload);

            return Ok();
        }
    }
}