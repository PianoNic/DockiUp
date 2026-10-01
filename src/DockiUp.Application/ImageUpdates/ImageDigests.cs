using System.Text.RegularExpressions;

namespace DockiUp.Application.ImageUpdates
{
    /// <summary>Pure helpers for comparing a local image with its registry, and for ordering tags.</summary>
    public static partial class ImageDigests
    {
        /// <summary>The digest the local image was pulled by for <paramref name="image"/>'s repository,
        /// taken from RepoDigests (`repo@sha256:...`); falls back to any recorded digest, null when the
        /// image was built locally (no registry digest at all).</summary>
        public static string? CurrentDigest(ImageReference image, IEnumerable<string> repoDigests)
        {
            string? fallback = null;
            foreach (var entry in repoDigests)
            {
                if (!ImageReference.TryParse(entry, out var parsed) || parsed.Digest is null) continue;
                if (parsed.SameRepository(image)) return parsed.Digest;
                fallback ??= parsed.Digest;
            }
            return fallback;
        }

        /// <summary>True when the registry's digest for the tag differs from every digest the local image
        /// is known by. Comparing against all of them avoids false positives when an image was pulled under
        /// several names.</summary>
        public static bool IsNewer(string latestDigest, IEnumerable<string> repoDigests)
            => !repoDigests.Any(d => d.EndsWith("@" + latestDigest, StringComparison.Ordinal));

        [GeneratedRegex(@"^v?(\d+(?:\.\d+)*)(.*)$")]
        private static partial Regex VersionTag();

        /// <summary>Registries list tags alphabetically with no dates, so "newest first" is a heuristic:
        /// `latest`, then version-like tags by version descending (a floating `1.27` before `1.27.3`, a plain
        /// `1.27.3` before `1.27.3-alpine`), then everything else alphabetically.</summary>
        public static List<string> NewestFirst(IEnumerable<string> tags)
        {
            var versions = new List<(string Tag, long[] Parts, string Suffix)>();
            var others = new List<string>();
            var latest = false;
            foreach (var tag in tags.Distinct(StringComparer.Ordinal))
            {
                if (tag == "latest") { latest = true; continue; }
                var m = VersionTag().Match(tag);
                if (m.Success && m.Groups[1].Value.Split('.').All(p => p.Length <= 18))
                    versions.Add((tag, m.Groups[1].Value.Split('.').Select(long.Parse).ToArray(), m.Groups[2].Value));
                else
                    others.Add(tag);
            }

            versions.Sort((a, b) =>
            {
                for (var i = 0; i < Math.Max(a.Parts.Length, b.Parts.Length); i++)
                {
                    // A missing component sorts above any number: `1.27` floats over every `1.27.x`.
                    var x = i < a.Parts.Length ? a.Parts[i] : long.MaxValue;
                    var y = i < b.Parts.Length ? b.Parts[i] : long.MaxValue;
                    if (x != y) return y.CompareTo(x);
                }
                if (a.Suffix.Length == 0 != (b.Suffix.Length == 0)) return a.Suffix.Length == 0 ? -1 : 1;
                return string.CompareOrdinal(a.Suffix, b.Suffix);
            });
            others.Sort(StringComparer.Ordinal);

            var result = new List<string>(versions.Count + others.Count + 1);
            if (latest) result.Add("latest");
            result.AddRange(versions.Select(v => v.Tag));
            result.AddRange(others);
            return result;
        }
    }
}
