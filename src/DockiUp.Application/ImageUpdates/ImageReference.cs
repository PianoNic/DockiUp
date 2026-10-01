using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace DockiUp.Application.ImageUpdates
{
    /// <summary>A parsed image reference (`[registry/]repository[:tag][@digest]`) with Docker's defaults
    /// applied: no registry means Docker Hub, a single-segment Hub name lives under `library/`, no tag means
    /// `latest`.</summary>
    public sealed partial record ImageReference(string Registry, string Repository, string Tag, string? Digest = null)
    {
        public const string DockerHub = "docker.io";

        /// <summary>Host that serves the registry API (Docker Hub's API is not on docker.io itself).</summary>
        public string ApiHost => Registry == DockerHub ? "registry-1.docker.io" : Registry;

        /// <summary>Repository as people write it (`nginx`, `ghcr.io/owner/app`), for building `repo:tag`.</summary>
        public string Name => Registry == DockerHub
            ? (Repository.StartsWith("library/", StringComparison.Ordinal) ? Repository["library/".Length..] : Repository)
            : $"{Registry}/{Repository}";

        public ImageReference WithTag(string tag) => this with { Tag = tag, Digest = null };

        public override string ToString() => Digest is null ? $"{Name}:{Tag}" : $"{Name}@{Digest}";

        // Docker's tag grammar: up to 128 chars, word char first, then word chars, dots and dashes.
        [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")]
        private static partial Regex TagPattern();

        [GeneratedRegex(@"^[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*$")]
        private static partial Regex RepositoryPattern();

        public static bool IsValidTag(string? tag) => tag is not null && TagPattern().IsMatch(tag);

        public static bool TryParse(string? reference, [NotNullWhen(true)] out ImageReference? image)
        {
            image = null;
            if (string.IsNullOrWhiteSpace(reference)) return false;
            var rest = reference.Trim();

            string? digest = null;
            var at = rest.IndexOf('@');
            if (at >= 0)
            {
                digest = rest[(at + 1)..];
                rest = rest[..at];
                if (!digest.Contains(':')) return false;
            }

            // A colon after the last slash is the tag separator; one before it belongs to a registry port.
            var tag = "latest";
            var colon = rest.LastIndexOf(':');
            if (colon > rest.LastIndexOf('/'))
            {
                tag = rest[(colon + 1)..];
                rest = rest[..colon];
                if (!IsValidTag(tag)) return false;
            }

            // The first segment is a registry host only when it looks like one (has a dot or port, or is localhost).
            var registry = DockerHub;
            var slash = rest.IndexOf('/');
            if (slash > 0)
            {
                var first = rest[..slash];
                if (first.Contains('.') || first.Contains(':') || first == "localhost")
                {
                    registry = first.ToLowerInvariant();
                    rest = rest[(slash + 1)..];
                }
            }
            if (registry is "index.docker.io" or "registry-1.docker.io") registry = DockerHub;
            if (registry == DockerHub && !rest.Contains('/')) rest = "library/" + rest;
            if (!RepositoryPattern().IsMatch(rest)) return false;

            image = new ImageReference(registry, rest, tag, digest);
            return true;
        }

        /// <summary>Same registry and repository (tags and digests ignored).</summary>
        public bool SameRepository(ImageReference other) => Registry == other.Registry && Repository == other.Repository;
    }
}
