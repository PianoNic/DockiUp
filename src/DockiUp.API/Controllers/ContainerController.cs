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

        [HttpPost("GitHub", Name = "GitHub")]
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
    }
}
