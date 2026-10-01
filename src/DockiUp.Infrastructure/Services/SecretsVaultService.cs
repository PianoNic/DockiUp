using System.Security.Cryptography;
using System.Text;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DockiUp.Infrastructure.Services
{
    public class SecretsVaultService(IDockiUpDbContext db, IConfiguration configuration) : ISecretsVaultService
    {
        private readonly byte[] _masterKey = LoadMasterKey(configuration);

        public async Task StoreAsync(string name, string plaintext, CancellationToken cancellationToken = default)
        {
            var (ciphertext, nonce, tag) = Encrypt(plaintext);

            var existing = await db.Secrets.SingleOrDefaultAsync(s => s.Name == name, cancellationToken);
            if (existing is null)
            {
                db.Secrets.Add(new Secret
                {
                    Name = name,
                    Ciphertext = ciphertext,
                    Nonce = nonce,
                    Tag = tag,
                });
            }
            else
            {
                existing.Ciphertext = ciphertext;
                existing.Nonce = nonce;
                existing.Tag = tag;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        public async Task<string?> RetrieveAsync(string name, CancellationToken cancellationToken = default)
        {
            var secret = await db.Secrets.SingleOrDefaultAsync(s => s.Name == name, cancellationToken);
            return secret is null ? null : Decrypt(secret.Ciphertext, secret.Nonce, secret.Tag);
        }

        public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
        {
            var secret = await db.Secrets.SingleOrDefaultAsync(s => s.Name == name, cancellationToken);
            if (secret is null)
            {
                return false;
            }

            db.Secrets.Remove(secret);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<IReadOnlyList<SecretDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var rows = await db.Secrets
                .OrderBy(s => s.Name)
                .Select(s => new SecretDto { Id = s.Id, Name = s.Name, CreatedAt = s.CreatedAt })
                .ToListAsync(cancellationToken);
            return rows;
        }

        // Layout: base64(nonce | tag | ciphertext). Nonce and tag have fixed sizes, so no separators needed.
        string ISecretsVaultService.Encrypt(string plaintext)
        {
            var (ciphertext, nonce, tag) = Encrypt(plaintext);
            return Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
        }

        string ISecretsVaultService.Decrypt(string encrypted)
        {
            var bytes = Convert.FromBase64String(encrypted);
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;
            return Decrypt(bytes[(nonceSize + tagSize)..], bytes[..nonceSize], bytes[nonceSize..(nonceSize + tagSize)]);
        }

        private (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(string plaintext)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[plainBytes.Length];
            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];

            using var gcm = new AesGcm(_masterKey, AesGcm.TagByteSizes.MaxSize);
            gcm.Encrypt(nonce, plainBytes, ciphertext, tag);

            return (ciphertext, nonce, tag);
        }

        private string Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag)
        {
            var plainBytes = new byte[ciphertext.Length];

            using var gcm = new AesGcm(_masterKey, AesGcm.TagByteSizes.MaxSize);
            gcm.Decrypt(nonce, ciphertext, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }

        private static byte[] LoadMasterKey(IConfiguration configuration)
        {
            var encoded = configuration["Vault:MasterKey"]
                ?? throw new InvalidOperationException("Vault:MasterKey is not configured.");

            byte[] key;
            try
            {
                key = Convert.FromBase64String(encoded);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Vault:MasterKey must be a base64-encoded value.", ex);
            }

            if (key.Length != 32)
            {
                throw new InvalidOperationException("Vault:MasterKey must decode to 32 bytes (256-bit AES key).");
            }

            return key;
        }
    }
}
