using DockiUp.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Git
{
    /// <summary>Decrypted HTTPS credentials for a git remote, handed to clone/fetch/push. Travels to nodes
    /// inside the RPC payload only - never in a URL or a log line.</summary>
    public sealed record GitCredentials(string Username, string Token)
    {
        // A record's generated ToString prints every member; never let the token reach a log that way.
        public override string ToString() => $"GitCredentials {{ Username = {Username}, Token = *** }}";
    }

    /// <summary>The one place git operations get their credentials from: the project's stored credential
    /// (by id), decrypted. Null id means a public repository.</summary>
    public interface IGitCredentialsProvider
    {
        /// <summary>Throws <see cref="ArgumentException"/> when <paramref name="credentialId"/> names no stored credential.</summary>
        Task<GitCredentials?> GetAsync(Guid? credentialId, CancellationToken cancellationToken = default);
    }

    public sealed class GitCredentialsProvider(IDockiUpDbContext db, ISecretsVaultService vault) : IGitCredentialsProvider
    {
        public async Task<GitCredentials?> GetAsync(Guid? credentialId, CancellationToken cancellationToken = default)
        {
            if (credentialId is not { } id) return null;
            var row = await db.GitCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new ArgumentException("The selected git credential no longer exists.");
            return new GitCredentials(row.Username, vault.Decrypt(row.TokenEncrypted));
        }
    }
}
