using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Controllers
{
    /// <summary>Encrypted secret storage (AES-256-GCM). Plaintext is write-only via the API: it is
    /// never returned by <c>List</c>; a freshly generated value is shown once on create.</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SecretsController(ISecretsVaultService vault, ISecretGeneratorService generator, IDockiUpDbContext? db = null) : ControllerBase
    {
        public record StoreSecretRequest(string Name, string? Value);
        public record GeneratedSecretDto(string Name, string Value);

        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<SecretDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
            => Ok(await vault.ListAsync(cancellationToken));

        /// <summary>Stores a secret. If no value is supplied a random one is generated and returned
        /// once in the response; otherwise responds 204.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(GeneratedSecretDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Store([FromBody] StoreSecretRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("A name is required.");

            if (string.IsNullOrEmpty(request.Value))
            {
                var generated = generator.Generate();
                await vault.StoreAsync(request.Name, generated, cancellationToken);
                return Ok(new GeneratedSecretDto(request.Name, generated));
            }

            await vault.StoreAsync(request.Name, request.Value, cancellationToken);
            return NoContent();
        }

        /// <summary>Refused while a project maps an env var to the secret (its next deploy would lack it).</summary>
        [HttpDelete("{name}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(string name, CancellationToken cancellationToken)
        {
            if (db is not null)
            {
                var users = await db.ProjectSecrets
                    .Join(db.Secrets.Where(s => s.Name == name), m => m.SecretId, s => s.Id, (m, s) => m.ProjectId)
                    .Join(db.ProjectInfo, id => id, p => p.Id, (id, p) => p.ProjectName)
                    .Distinct().ToListAsync(cancellationToken);
                if (users.Count > 0)
                    throw new ArgumentException($"'{name}' is used by {string.Join(", ", users)}. Remove it from their secrets first.");
            }
            return await vault.DeleteAsync(name, cancellationToken) ? NoContent() : NotFound();
        }
    }
}
