using System.Text.Json;
using System.Text.RegularExpressions;
using DockiUp.Application.Dtos;

namespace DockiUp.Application.Compose
{
    /// <summary>Reads the result of `docker compose config --format json`: the services on success, and
    /// compose's messages split into errors and warnings. Only names and images are returned, never the
    /// resolved file - it may contain interpolated secrets.</summary>
    public static partial class ComposeValidation
    {
        public static ComposeValidationDto Parse(int exitCode, string stdout, string stderr)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            foreach (var raw in stderr.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var warning = line.StartsWith("WARN", StringComparison.Ordinal) || line.Contains("level=warning");
                (warning ? warnings : errors).Add(Message(line));
            }

            if (exitCode != 0)
            {
                if (errors.Count == 0) errors.Add($"docker compose config failed (exit {exitCode}).");
                return new ComposeValidationDto(false, [.. errors], [.. warnings], []);
            }

            var services = new List<ComposeServiceDto>();
            try
            {
                using var doc = JsonDocument.Parse(stdout);
                if (doc.RootElement.TryGetProperty("services", out var map) && map.ValueKind == JsonValueKind.Object)
                {
                    foreach (var service in map.EnumerateObject())
                    {
                        var image = service.Value.ValueKind == JsonValueKind.Object && service.Value.TryGetProperty("image", out var img)
                            ? img.GetString() : null;
                        services.Add(new ComposeServiceDto(service.Name, image));
                    }
                }
            }
            catch (JsonException)
            {
                return new ComposeValidationDto(false, ["docker compose config returned output DockiUp could not read."], [.. warnings], []);
            }
            if (services.Count == 0)
                return new ComposeValidationDto(false, ["The compose file defines no services."], [.. warnings], []);
            return new ComposeValidationDto(true, [], [.. warnings], [.. services]);
        }

        // Strip compose's logger framing: `WARN[0000] text` and `time="..." level=warning msg="text"`.
        private static string Message(string line)
        {
            var msg = LogfmtMessage().Match(line);
            if (msg.Success) return msg.Groups[1].Value.Replace("\\\"", "\"");
            var bracket = BracketPrefix().Match(line);
            return bracket.Success ? line[bracket.Length..].Trim() : line;
        }

        [GeneratedRegex("msg=\"((?:[^\"\\\\]|\\\\.)*)\"")]
        private static partial Regex LogfmtMessage();

        [GeneratedRegex(@"^[A-Z]+\[\d+\]")]
        private static partial Regex BracketPrefix();
    }
}
