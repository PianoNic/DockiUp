using System.Text.RegularExpressions;
using DockiUp.Application.Dtos;

namespace DockiUp.Application.Compose
{
    /// <summary>Builds a one-service compose file from the image form, so an image project deploys, updates
    /// and keeps history like any other compose project.</summary>
    public static partial class ImageComposeGenerator
    {
        public static readonly string[] RestartPolicies = ["no", "always", "unless-stopped", "on-failure"];

        public static GeneratedComposeDto Generate(ImageProjectDto dto)
        {
            var image = dto.Image?.Trim() ?? "";
            if (!ImagePattern().IsMatch(image))
                throw new ArgumentException($"'{image}' is not a valid image reference (e.g. nginx, ghcr.io/owner/app).");

            var tag = dto.Tag?.Trim();
            if (!string.IsNullOrEmpty(tag))
            {
                if (!TagPattern().IsMatch(tag))
                    throw new ArgumentException($"'{tag}' is not a valid tag.");
                if (HasTagOrDigest(image))
                    throw new ArgumentException("Give the tag either in the image or in the tag field, not both.");
                image += ":" + tag;
            }

            var spec = new ComposeServiceSpec { Name = ComposeServiceSpec.ServiceNameFrom(image), Image = image };

            foreach (var port in Clean(dto.Ports))
            {
                if (!PortPattern().IsMatch(port))
                    throw new ArgumentException($"Port '{port}' must look like 8080:80, 8080:80/udp or 127.0.0.1:8080:80.");
                spec.Ports.Add(port);
            }
            foreach (var volume in Clean(dto.Volumes))
            {
                var parts = volume.Split(':');
                if (parts.Length is < 2 or > 3 || parts[0].Length == 0 || !parts[1].StartsWith('/') || (parts.Length == 3 && parts[2] is not ("ro" or "rw")))
                    throw new ArgumentException($"Volume '{volume}' must look like ./data:/data, my-volume:/data or /srv/x:/x:ro.");
                spec.Volumes.Add(volume);
            }
            foreach (var env in Clean(dto.Environment))
            {
                var key = env.Split('=', 2)[0];
                if (!EnvKeyPattern().IsMatch(key))
                    throw new ArgumentException($"'{key}' is not a valid environment variable name.");
                spec.Environment.Add(env);
            }

            var restart = dto.Restart?.Trim();
            if (!string.IsNullOrEmpty(restart))
            {
                if (!RestartPolicies.Contains(restart) && !RestartOnFailurePattern().IsMatch(restart))
                    throw new ArgumentException($"Restart policy must be one of {string.Join(", ", RestartPolicies)}.");
                spec.Restart = restart;
            }
            return new GeneratedComposeDto(spec.ToYaml(), []);
        }

        private static IEnumerable<string> Clean(string[]? values)
            => (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0);

        // A ':' after the last '/' is a tag (a ':' before it is a registry port); '@' is a digest.
        private static bool HasTagOrDigest(string image)
            => image.Contains('@') || image[(image.LastIndexOf('/') + 1)..].Contains(':');

        [GeneratedRegex(@"^[a-z0-9]+([._-][a-z0-9]+)*(:[0-9]+)?(/[a-z0-9]+([._-]+[a-z0-9]+)*)*(:[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})?(@sha256:[a-f0-9]{64})?$")]
        private static partial Regex ImagePattern();

        [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")]
        private static partial Regex TagPattern();

        [GeneratedRegex(@"^((\d{1,3}\.){3}\d{1,3}:)?(\d{1,5}(-\d{1,5})?:)?\d{1,5}(-\d{1,5})?(/(tcp|udp|sctp))?$")]
        private static partial Regex PortPattern();

        [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$")]
        private static partial Regex EnvKeyPattern();

        [GeneratedRegex(@"^on-failure:\d+$")]
        private static partial Regex RestartOnFailurePattern();
    }
}
