using DockiUp.API.Authorisation;
using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        [HttpPost("github")]
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
    }
}