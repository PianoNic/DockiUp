using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DockiUp.Application.Deployments;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Controllers
{
    /// <summary>Git push webhook: POST /api/Webhook/{projectId}. Works with GitHub, Gitea/Forgejo,
    /// GitLab, or anything that can send a header.</summary>
    // Anonymous: git providers can't carry a user token; the per-project secret is the credential.
    [AllowAnonymous]
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController(IMediator mediator, IDockiUpDbContext db) : ControllerBase
    {
        /// <summary>Authenticates with one of: X-Hub-Signature-256 (GitHub/Gitea HMAC-SHA256 of the body),
        /// X-Gitea-Signature (hex HMAC), X-Gitlab-Token, or X-Webhook-Secret. Pushes to another branch
        /// than the project tracks are ignored. Returns 202 at once; the deploy runs in the queue.</summary>
        [HttpPost("{projectId:guid}")]
        [BufferBody]
        [ProducesResponseType(typeof(DeploymentDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Trigger(Guid projectId)
        {
            var project = await db.ProjectInfo.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, HttpContext.RequestAborted);
            if (project is null) return NotFound();
            if (string.IsNullOrEmpty(project.WebhookSecret)) return Unauthorized("This project has no webhook secret.");

            // The raw bytes are what the provider signed. Form-encoded payloads have already been read by
            // model binding by now, so rewind the buffered body (see BufferBodyAttribute).
            Request.Body.Position = 0;
            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, HttpContext.RequestAborted);
            var body = buffer.ToArray();

            var provider = Authenticate(Request.Headers, body, project.WebhookSecret);
            if (provider is null) return Unauthorized("Missing or invalid webhook signature/secret.");

            if (Request.Headers["X-GitHub-Event"] == "ping" || Request.Headers["X-Gitea-Event"] == "ping")
                return Ok(new { message = "pong" });

            var pushedRef = TryReadRef(body);
            if (pushedRef is not null && project.Branch is not null && pushedRef != $"refs/heads/{project.Branch}")
                return Accepted(new { ignored = $"Push to {pushedRef}; this project tracks {project.Branch}." });

            var deployment = await mediator.Send(
                new QueueDeploymentCommand(project.Id, DeploymentTrigger.Webhook, $"{provider} webhook"), HttpContext.RequestAborted);
            return Accepted(deployment);
        }

        /// <summary>Returns the provider name when one of the supported credentials matches, else null.</summary>
        internal static string? Authenticate(IHeaderDictionary headers, byte[] body, string secret)
        {
            var hmacHex = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

            string hubSignature = headers["X-Hub-Signature-256"].ToString();
            if (hubSignature.StartsWith("sha256=", StringComparison.Ordinal))
                return FixedEquals(hubSignature["sha256=".Length..].ToLowerInvariant(), hmacHex)
                    ? (headers.ContainsKey("X-Gitea-Event") ? "Gitea" : "GitHub") : null;

            string giteaSignature = headers["X-Gitea-Signature"].ToString();
            if (giteaSignature.Length > 0) return FixedEquals(giteaSignature.ToLowerInvariant(), hmacHex) ? "Gitea" : null;

            string gitlabToken = headers["X-Gitlab-Token"].ToString();
            if (gitlabToken.Length > 0) return FixedEquals(gitlabToken, secret) ? "GitLab" : null;

            string plain = headers["X-Webhook-Secret"].ToString();
            if (plain.Length > 0) return FixedEquals(plain, secret) ? "Custom" : null;

            return null;
        }

        private static bool FixedEquals(string a, string b)
            => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

        // Push payloads from GitHub, Gitea and GitLab all carry the pushed ref as top-level "ref".
        private static string? TryReadRef(byte[] body)
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                return json.RootElement.ValueKind == JsonValueKind.Object
                       && json.RootElement.TryGetProperty("ref", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString()
                    : null;
            }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>Buffers the request body before model binding, which would otherwise consume a
    /// form-encoded webhook payload (GitHub's "application/x-www-form-urlencoded" option) and leave
    /// nothing to verify the signature against.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class BufferBodyAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context) => context.HttpContext.Request.EnableBuffering();

        public void OnResourceExecuted(ResourceExecutedContext context) { }
    }
}
