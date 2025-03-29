using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text;

namespace DockiUp.API.Authorisation
{
    [AttributeUsage(AttributeTargets.Method)]
    public class GitHubWebhookAuthAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string _secret;

        public GitHubWebhookAuthAttribute(string secret)
        {
            _secret = secret;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // Check if signature header exists
            if (!context.HttpContext.Request.Headers.TryGetValue("X-Hub-Signature-256", out var signatureWithPrefix))
            {
                context.Result = new BadRequestObjectResult("No signature provided");
                return;
            }

            string signature = signatureWithPrefix.ToString().Substring(7); // Remove "sha256=" prefix

            // Read and verify the request body
            var originalBodyStream = context.HttpContext.Request.Body;

            try
            {
                // Enable buffering so we can read the body multiple times
                context.HttpContext.Request.EnableBuffering();

                // Read the request body
                using (var reader = new StreamReader(
                    context.HttpContext.Request.Body,
                    encoding: Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true))
                {
                    var payload = await reader.ReadToEndAsync();

                    // Verify the signature
                    bool isValid = VerifySignature(payload, signature, _secret);

                    if (!isValid)
                    {
                        context.Result = new UnauthorizedObjectResult("Invalid signature");
                        return;
                    }

                    // Reset the request body stream position for the controller to read it
                    context.HttpContext.Request.Body.Position = 0;
                }
            }
            finally
            {
                // Ensure we reset the body stream position
                context.HttpContext.Request.Body.Position = 0;
            }
        }

        private bool VerifySignature(string payload, string signature, string secret)
        {
            using (var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                string computedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return string.Equals(computedSignature, signature, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
