using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DockiUp.Application.Compose
{
    /// <summary>The subset of a compose service DockiUp can generate (from an image form or a `docker run`
    /// command), and its YAML rendering.</summary>
    public sealed partial class ComposeServiceSpec
    {
        public string Name { get; set; } = "app";
        public string Image { get; set; } = "";
        public string? ContainerName { get; set; }
        public string? Hostname { get; set; }
        public string? User { get; set; }
        public string? WorkingDir { get; set; }
        public List<string> Entrypoint { get; } = [];
        public List<string> Command { get; } = [];
        public string? Restart { get; set; }
        public string? NetworkMode { get; set; }
        public List<string> Networks { get; } = [];
        public List<string> Ports { get; } = [];
        public List<string> Volumes { get; } = [];
        public List<string> Environment { get; } = [];
        public List<string> EnvFiles { get; } = [];
        public List<string> Labels { get; } = [];
        public bool Privileged { get; set; }
        public bool StdinOpen { get; set; }
        public bool Tty { get; set; }

        /// <summary>A compose service name from anything (container name, image): lowercase [a-z0-9_-].</summary>
        public static string ServiceNameFrom(string value)
        {
            var name = value.Split('@')[0];
            name = name[(name.LastIndexOf('/') + 1)..].Split(':')[0];
            name = InvalidNameChars().Replace(name.ToLowerInvariant(), "-").Trim('-', '_', '.');
            return name.Length == 0 ? "app" : name;
        }

        /// <summary>Renders a compose file with this one service. Every scalar is double-quoted (JSON string
        /// syntax is valid YAML), so ports like 22:22 never turn into numbers and odd values stay intact.</summary>
        public string ToYaml()
        {
            var y = new StringBuilder();
            y.Append("services:\n");
            y.Append($"  {Name}:\n");
            Scalar(y, "image", Image);
            Scalar(y, "container_name", ContainerName);
            Scalar(y, "hostname", Hostname);
            Scalar(y, "user", User);
            Scalar(y, "working_dir", WorkingDir);
            List(y, "entrypoint", Entrypoint);
            List(y, "command", Command);
            Scalar(y, "restart", Restart);
            if (Privileged) y.Append("    privileged: true\n");
            if (StdinOpen) y.Append("    stdin_open: true\n");
            if (Tty) y.Append("    tty: true\n");
            Scalar(y, "network_mode", NetworkMode);
            List(y, "networks", Networks);
            List(y, "ports", Ports);
            List(y, "volumes", Volumes);
            List(y, "env_file", EnvFiles);
            List(y, "environment", Environment);
            List(y, "labels", Labels);

            // User-defined networks given to `docker run` already exist on the host.
            if (Networks.Count > 0)
            {
                y.Append("\nnetworks:\n");
                foreach (var network in Networks.Distinct())
                    y.Append($"  {Quote(network)}:\n    external: true\n");
            }

            // Named volumes (not host paths) must be declared at the top level.
            var named = Volumes.Select(v => v.Split(':')[0]).Where(IsNamedVolume).Distinct().ToList();
            if (named.Count > 0)
            {
                y.Append("\nvolumes:\n");
                foreach (var volume in named)
                    y.Append($"  {Quote(volume)}: {{}}\n");
            }
            return y.ToString();
        }

        // A volume source without a path separator (and not '.' / '~') is a named volume.
        private static bool IsNamedVolume(string source)
            => source.Length > 0 && !source.Contains('/') && !source.Contains('\\') && source != "." && source != "~" && !source.StartsWith('$');

        private static void Scalar(StringBuilder y, string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) y.Append($"    {key}: {Quote(value)}\n");
        }

        private static void List(StringBuilder y, string key, List<string> values)
        {
            if (values.Count == 0) return;
            y.Append($"    {key}:\n");
            foreach (var value in values) y.Append($"      - {Quote(value)}\n");
        }

        private static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static string Quote(string value) => JsonSerializer.Serialize(value, Readable);

        [GeneratedRegex("[^a-z0-9_.-]+")]
        private static partial Regex InvalidNameChars();
    }
}
