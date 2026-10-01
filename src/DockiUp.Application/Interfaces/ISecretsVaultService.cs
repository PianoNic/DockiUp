using DockiUp.Application.Dtos;

namespace DockiUp.Application.Interfaces
{
    /// <summary>Stores named secrets encrypted at rest with the Vault master key.</summary>
    public interface ISecretsVaultService
    {
        Task StoreAsync(string name, string plaintext, CancellationToken cancellationToken = default);
        Task<string?> RetrieveAsync(string name, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);
        /// <summary>Secret metadata (names, never plaintext) for listing.</summary>
        Task<IReadOnlyList<SecretDto>> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>Encrypts a value with the vault master key into a self-contained string, for credentials
        /// kept on other rows (notification channels, git credentials) rather than as named secrets.</summary>
        string Encrypt(string plaintext);
        /// <summary>Reverses <see cref="Encrypt"/>.</summary>
        string Decrypt(string encrypted);
    }
}
