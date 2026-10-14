using System.Text.RegularExpressions;
using DockiUp.Application.Interfaces;

namespace DockiUp.Application.ImageUpdates
{
    /// <summary>What a floating tag (`latest`, `6`, `17-alpine`) points at: the most specific version tag of the
    /// same image with the same digest, e.g. `6.0.3`. Registries don't say this directly, so it is looked up.</summary>
    public static partial class ImageVersions
    {
        // ponytail: the newest few candidates only. An image whose digest is older than those shows no version.
        private const int MaxLookups = 25;

        [GeneratedRegex(@"^v?(\d+(?:\.\d+)*)(.*)$")]
        private static partial Regex VersionTag();

        /// <summary>The version tags <paramref name="tag"/> may stand for, newest first: for `6` the `6.x` ones,
        /// for `17-alpine` the `17.x-alpine` ones, for `latest` (or another word) every plain version.</summary>
        public static List<string> Candidates(string tag, IEnumerable<string> tags)
        {
            var m = VersionTag().Match(tag);
            var (prefix, suffix) = m.Success ? (m.Groups[1].Value + ".", m.Groups[2].Value) : ("", "");
            return [.. ImageDigests.NewestFirst(tags).Where(t =>
            {
                var c = VersionTag().Match(t);
                return c.Success && c.Groups[2].Value == suffix && (c.Groups[1].Value + ".").StartsWith(prefix, StringComparison.Ordinal)
                    && c.Groups[1].Value + "." != prefix;
            })];
        }

        /// <summary>The most specific version tag whose digest is <paramref name="digest"/>; "" when none of the
        /// newest candidates matches (so it isn't looked up again for the same digest).</summary>
        public static async Task<string> ResolveAsync(IRegistryClient registry, ImageReference image, string digest, CancellationToken cancellationToken)
        {
            var candidates = Candidates(image.Tag, await registry.ListTagsAsync(image, cancellationToken: cancellationToken) ?? []);
            string? found = null;
            var lookups = 0;
            foreach (var tag in candidates)
            {
                // Once `6.0` matches, only `6.0.x` can be more specific.
                if (found is not null && !tag.StartsWith(found + ".", StringComparison.Ordinal)) continue;
                if (++lookups > MaxLookups) break;
                if (await registry.GetDigestAsync(image.WithTag(tag), cancellationToken) == digest) found = tag;
            }
            return found ?? "";
        }
    }
}
