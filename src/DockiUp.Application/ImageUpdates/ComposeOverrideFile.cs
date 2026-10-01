using DockiUp.Application.Dtos;
using YamlDotNet.Serialization;

namespace DockiUp.Application.ImageUpdates
{
    /// <summary>`dockiup.override.yml` in the project folder: a compose override DockiUp owns, so a chosen
    /// image tag never touches the user's (or the repo's) compose file. Compose merges it in through an
    /// extra `-f` whenever it exists (DockerService.ComposeProjectArgs). Git sync keeps it (untracked files survive the hard reset).</summary>
    public static class ComposeOverrideFile
    {
        public const string FileName = "dockiup.override.yml";

        private const string Header = "# Managed by DockiUp: image tags chosen in the UI. Reset them there rather than editing this file.\n";

        /// <summary>service -> image pinned in the override (empty for no file / no pins).</summary>
        public static Dictionary<string, string> ReadImages(string? yaml)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Load(yaml) is { } root && root.TryGetValue("services", out var s) && s is Dictionary<object, object> services)
                foreach (var (name, value) in services)
                    if (value is Dictionary<object, object> svc && svc.TryGetValue("image", out var image) && image is string img)
                        result[name.ToString()!] = img;
            return result;
        }

        /// <summary>Sets (or with null removes) `services.&lt;service&gt;.image`, keeping everything else in the
        /// file. Returns the new content, or null when nothing is left and the file should be deleted.</summary>
        public static string? SetImage(string? yaml, string service, string? image)
        {
            var root = Load(yaml) ?? [];
            if (!root.TryGetValue("services", out var s) || s is not Dictionary<object, object> services)
                root["services"] = services = [];

            if (!services.TryGetValue(service, out var v) || v is not Dictionary<object, object> svc)
                services[service] = svc = [];

            if (image is null) svc.Remove("image");
            else svc["image"] = image;

            if (svc.Count == 0) services.Remove(service);
            if (services.Count == 0) root.Remove("services");
            if (root.Count == 0) return null;

            return Header + new SerializerBuilder().Build().Serialize(root);
        }

        private static Dictionary<object, object>? Load(string? yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml)) return null;
            try
            {
                return new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>?>(yaml);
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                throw new ArgumentException($"{FileName} is not valid YAML: {ex.Message}");
            }
        }
    }
}
