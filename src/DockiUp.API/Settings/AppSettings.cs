using DockiUp.Domain;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace DockiUp.API.Settings
{
    /// <summary>Settings saved in the UI, as a configuration layer right below environment variables: everything
    /// that reads IConfiguration sees them, and a value set in the environment always wins (and locks the field).</summary>
    public sealed class DatabaseSettingsProvider : ConfigurationProvider, IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

        public void Set(IEnumerable<AppSetting> settings)
        {
            Data = settings.ToDictionary(s => s.Key, s => (string?)s.Value, StringComparer.OrdinalIgnoreCase);
            OnReload();
        }

        /// <summary>Adds the layer just below the environment variables.</summary>
        public static DatabaseSettingsProvider AddTo(IConfigurationManager configuration)
        {
            var provider = new DatabaseSettingsProvider();
            // The unprefixed one: ASPNETCORE_/DOTNET_ sources come first, below appsettings.json.
            var env = configuration.Sources.ToList().FindLastIndex(s => s is EnvironmentVariablesConfigurationSource { Prefix: null or "" });
            configuration.Sources.Insert(env < 0 ? configuration.Sources.Count : env, provider);
            return provider;
        }
    }

    public enum SettingType { Number, Url, Duration }

    /// <summary>A setting the UI can change. <paramref name="EnvNames"/> are the variables that set (and lock) it.</summary>
    public sealed record SettingDefinition(string Key, string Group, string Label, string Hint, SettingType Type, string? Default, double? Min, string[] EnvNames);

    public static class AppSettings
    {
        // ponytail: only settings DockiUp reads while running. Database, vault key, sign-in, paths and CORS are needed
        // before the UI exists (or guard it), so they stay environment-only.
        public static readonly SettingDefinition[] All =
        [
            new("DockiUp:PublicUrl", "General", "Public address",
                "Where DockiUp is reached, e.g. https://dockiup.example.com. Used for node setup, webhook URLs and AI assistant sign-in (that one after a restart).",
                SettingType.Url, null, null, ["DockiUp__PublicUrl", "PUBLIC_URL", "Oidc__PublicUrl"]),
            new("ImageUpdates:Interval", "Image updates", "Check every",
                "How often running images are compared with their registry, e.g. 30s, 5m, 6h or 1d. 0 turns it off; Check now still works. A project can set its own in its Images tab.",
                SettingType.Duration, "6h", null, ["ImageUpdates__Interval"]),
            new("Stats:IntervalSeconds", "Monitoring", "Sample every (seconds)",
                "How often CPU and memory of every container are read.",
                SettingType.Number, "15", 5, ["Stats__IntervalSeconds"]),
            new("Stats:RetentionHours", "Monitoring", "Keep history (hours)",
                "How long the per-minute CPU and memory history of containers is kept. Hosts keep a week.",
                SettingType.Number, "24", 1, ["Stats__RetentionHours"]),
            // Min is in seconds for durations: a speed test fills the line for about 15 seconds.
            new("SpeedTest:Interval", "Monitoring", "Internet speed test every",
                "How often each host measures its internet connection, e.g. 6h or 1d. A test moves up to a few hundred MB. 0 turns it off; Test now still works.",
                SettingType.Duration, "1d", 3600, ["SpeedTest__Interval"]),
        ];

        /// <summary>The address DockiUp is reached at, without a trailing slash; null when none is set.</summary>
        public static string? PublicUrl(IConfiguration configuration)
            => new[] { configuration["Oidc:PublicUrl"], configuration["DockiUp:PublicUrl"], Environment.GetEnvironmentVariable("PUBLIC_URL") }
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.TrimEnd('/');

        /// <summary>The environment variable that sets this setting, if any.</summary>
        public static string? LockedBy(SettingDefinition setting)
        {
            // Configuration ignores case (IMAGEUPDATES__INTERVALHOURS counts too), so this does as well.
            var set = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .Where(e => !string.IsNullOrWhiteSpace(e.Value as string)).Select(e => (string)e.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return setting.EnvNames.FirstOrDefault(set.Contains);
        }
    }
}
