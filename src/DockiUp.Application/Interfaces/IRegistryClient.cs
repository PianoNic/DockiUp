using DockiUp.Application.ImageUpdates;

namespace DockiUp.Application.Interfaces
{
    /// <summary>Reads image metadata from an OCI / Docker Registry HTTP API v2 registry.</summary>
    public interface IRegistryClient
    {
        /// <summary>The digest the image's tag points to right now - for a multi-arch image the index
        /// (manifest list) digest, which is what `docker pull` records in RepoDigests. Null when the tag
        /// does not exist.</summary>
        Task<string?> GetDigestAsync(ImageReference image, CancellationToken cancellationToken = default);

        /// <summary>The repository's tags in registry order, at most <paramref name="max"/>.</summary>
        Task<IReadOnlyList<string>> ListTagsAsync(ImageReference image, int max = 5000, CancellationToken cancellationToken = default);
    }

    public record RegistryCredentials(string Username, string Password);

    /// <summary>Seam for private registries: credentials for a registry host (e.g. "ghcr.io", "docker.io"),
    /// or null to go anonymous. The default implementation is always anonymous; register another
    /// implementation to supply stored credentials.</summary>
    public interface IRegistryCredentialsProvider
    {
        Task<RegistryCredentials?> GetAsync(string registry, CancellationToken cancellationToken = default);
    }
}
