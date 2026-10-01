using DockiUp.Application.Interfaces;
using DockiUp.Application.Notifications;
using DockiUp.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Controllers
{
    /// <summary>HTTPS credentials (user name + personal access token) for private git repositories. Tokens are
    /// stored vault-encrypted and only ever returned masked.</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class GitCredentialsController(IDockiUpDbContext db, ISecretsVaultService vault) : ControllerBase
    {
        public record GitCredentialDto(Guid Id, string Name, string Username, string TokenMasked, int ProjectCount, DateTime CreatedAt);

        /// <summary><see cref="Token"/> is required on create; on update null keeps the stored token.</summary>
        public record SaveGitCredentialRequest(string Name, string? Username, string? Token);

        [HttpGet(Name = "ListGitCredentials")]
        [ProducesResponseType(typeof(GitCredentialDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<GitCredentialDto[]>> ListGitCredentials(CancellationToken cancellationToken)
        {
            var rows = await db.GitCredentials.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
            var usage = await db.ProjectInfo.Where(p => p.GitCredentialId != null)
                .GroupBy(p => p.GitCredentialId!.Value).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
            return Ok(rows.Select(c => ToDto(c, usage.GetValueOrDefault(c.Id))).ToArray());
        }

        [HttpPost(Name = "CreateGitCredential")]
        [ProducesResponseType(typeof(GitCredentialDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<GitCredentialDto>> CreateGitCredential([FromBody] SaveGitCredentialRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Token)) throw new ArgumentException("A token is required.");
            var name = await ValidNameAsync(request.Name, null, cancellationToken);
            var credential = new GitCredential { Name = name, Username = Username(request), TokenEncrypted = vault.Encrypt(request.Token.Trim()) };
            db.GitCredentials.Add(credential);
            await db.SaveChangesAsync(cancellationToken);
            return Ok(ToDto(credential, 0));
        }

        [HttpPut("{id:guid}", Name = "UpdateGitCredential")]
        [ProducesResponseType(typeof(GitCredentialDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<GitCredentialDto>> UpdateGitCredential(Guid id, [FromBody] SaveGitCredentialRequest request, CancellationToken cancellationToken)
        {
            var credential = await db.GitCredentials.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Git credential not found.");
            credential.Name = await ValidNameAsync(request.Name, id, cancellationToken);
            credential.Username = Username(request);
            if (!string.IsNullOrWhiteSpace(request.Token)) credential.TokenEncrypted = vault.Encrypt(request.Token.Trim());
            await db.SaveChangesAsync(cancellationToken);
            return Ok(ToDto(credential, await db.ProjectInfo.CountAsync(p => p.GitCredentialId == id, cancellationToken)));
        }

        /// <summary>Refused while a project still uses the credential (its next fetch would fail).</summary>
        [HttpDelete("{id:guid}", Name = "DeleteGitCredential")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteGitCredential(Guid id, CancellationToken cancellationToken)
        {
            var credential = await db.GitCredentials.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Git credential not found.");
            var users = await db.ProjectInfo.Where(p => p.GitCredentialId == id).Select(p => p.ProjectName).ToListAsync(cancellationToken);
            if (users.Count > 0)
                throw new ArgumentException($"\"{credential.Name}\" is used by {string.Join(", ", users)}. Pick another credential there first.");
            db.GitCredentials.Remove(credential);
            await db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        private GitCredentialDto ToDto(GitCredential c, int projectCount)
            => new(c.Id, c.Name, c.Username, SecretMask.Mask(vault.Decrypt(c.TokenEncrypted)), projectCount, c.CreatedAt);

        private static string Username(SaveGitCredentialRequest request)
            => string.IsNullOrWhiteSpace(request.Username) ? "git" : request.Username.Trim();

        private async Task<string> ValidNameAsync(string? name, Guid? self, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name is required.");
            name = name.Trim();
            if (await db.GitCredentials.AnyAsync(c => c.Name == name && c.Id != self, cancellationToken))
                throw new ArgumentException($"A git credential named \"{name}\" already exists.");
            return name;
        }
    }
}
