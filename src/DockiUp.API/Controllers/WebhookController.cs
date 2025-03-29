using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        [HttpPost("github")]
        public async Task<IActionResult> GitHubWebhook()
        {
            Request.Headers.TryGetValue("X-Hub-Signature-256", out var signatureWithPrefix);

            if (string.IsNullOrEmpty(signatureWithPrefix))
            {
                return BadRequest("No signature provided");
            }

            string signature = signatureWithPrefix.ToString().Substring(7); // Remove "sha256=" prefix
            string secret = "your_github_webhook_secret";

            using (var memoryStream = new MemoryStream())
            {
                await Request.Body.CopyToAsync(memoryStream);
                memoryStream.Position = 0; // Reset the stream position

                using (var reader = new StreamReader(memoryStream))
                {
                    var payload = await reader.ReadToEndAsync();

                    bool isValid = VerifySignature(payload, signature, secret);

                    if (!isValid)
                    {
                        return Unauthorized("Invalid signature");
                    }

                    Console.WriteLine(payload);
                    memoryStream.Position = 0; // Reset for potential model binding later
                }
            }

            return Ok();
        }

        private bool VerifySignature(string payload, string signature, string secret)
        {
            using (var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret)))
            {
                byte[] hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
                string computedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

                return string.Equals(computedSignature, signature, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}