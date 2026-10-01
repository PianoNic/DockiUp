using DockiUp.Application.Dtos;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DockiUp.Application.Compose
{
    /// <summary>Finds the compose files in a checkout: *.yml / *.yaml that parse as YAML with a top-level
    /// `services` mapping (so CI configs, k8s manifests etc. are left out).</summary>
    public static class ComposeFiles
    {
        public const long MaxFileBytes = 256 * 1024;
        private const int MaxDepth = 6;
        private const int MaxFiles = 200;

        /// <summary>The names compose itself looks for, in its own order of preference.</summary>
        public static readonly string[] Preferred = ["compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml"];

        public static RepositoryComposeFileDto[] Find(string root)
        {
            var found = new List<RepositoryComposeFileDto>();
            Walk(root, root, 0, found);
            return [.. found
                .OrderBy(f => f.Path.Count(c => c == '/'))
                .ThenBy(f => Array.IndexOf(Preferred, f.Path) is var i && i >= 0 ? i : int.MaxValue)
                .ThenBy(f => f.Path, StringComparer.Ordinal)];
        }

        /// <summary>The file to preselect: a standard compose name at the root, else the first one found.</summary>
        public static string? DefaultFile(IReadOnlyCollection<string> paths)
            => Preferred.FirstOrDefault(paths.Contains) ?? paths.FirstOrDefault();

        /// <summary>The service names of a compose document, or null when it isn't one.</summary>
        public static string[]? ServicesOf(string yaml)
        {
            try
            {
                var stream = new YamlStream();
                stream.Load(new StringReader(yaml));
                if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
                    return null;
                return root.Children.TryGetValue(new YamlScalarNode("services"), out var services) && services is YamlMappingNode map
                    ? [.. map.Children.Keys.OfType<YamlScalarNode>().Select(k => k.Value ?? "")]
                    : null;
            }
            catch (YamlException)
            {
                return null;
            }
        }

        private static void Walk(string root, string dir, int depth, List<RepositoryComposeFileDto> found)
        {
            foreach (var file in Directory.EnumerateFiles(dir).Order(StringComparer.Ordinal))
            {
                if (found.Count >= MaxFiles) return;
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".yml", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (new FileInfo(file).Length > MaxFileBytes) continue;
                var content = File.ReadAllText(file);
                if (ServicesOf(content) is { } services)
                    found.Add(new RepositoryComposeFileDto(Path.GetRelativePath(root, file).Replace('\\', '/'), content, services));
            }
            if (depth >= MaxDepth) return;
            foreach (var sub in Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal))
            {
                // .git, .github (workflows are YAML too) and dependency folders hold no deployable compose files.
                var name = Path.GetFileName(sub);
                if (name.StartsWith('.') || name is "node_modules" or "vendor") continue;
                Walk(root, sub, depth + 1, found);
            }
        }
    }
}
