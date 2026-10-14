using System.Globalization;
using System.Text.RegularExpressions;

namespace DockiUp.Application
{
    /// <summary>Intervals as people write them: "30s", "5m", "6h", "1d", or "0" for off.</summary>
    public static partial class Durations
    {
        public static readonly TimeSpan Minimum = TimeSpan.FromSeconds(5);

        [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s*([smhd]?)\s*$", RegexOptions.IgnoreCase)]
        private static partial Regex Pattern();

        /// <summary>Null when the text isn't a duration. A bare number counts as hours.</summary>
        public static TimeSpan? Parse(string? text)
        {
            var m = Pattern().Match(text ?? "");
            if (!m.Success) return null;
            var n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return m.Groups[2].Value.ToLowerInvariant() switch
            {
                "s" => TimeSpan.FromSeconds(n),
                "m" => TimeSpan.FromMinutes(n),
                "d" => TimeSpan.FromDays(n),
                _ => TimeSpan.FromHours(n),
            };
        }

        /// <summary>Parses an interval and checks it: 0 (off) or at least <see cref="Minimum"/>.</summary>
        public static TimeSpan ParseInterval(string text, string what)
        {
            var d = Parse(text) ?? throw new ArgumentException($"{what} must look like 30s, 5m, 6h or 1d.");
            if (d != TimeSpan.Zero && d < Minimum) throw new ArgumentException($"{what} must be at least {Format(Minimum)}, or 0 to turn it off.");
            return d;
        }

        /// <summary>The largest whole unit: 300s is "5m", 90s stays "90s".</summary>
        public static string Format(TimeSpan d)
        {
            var s = (long)d.TotalSeconds;
            return s == 0 ? "0"
                : s % 86400 == 0 ? $"{s / 86400}d"
                : s % 3600 == 0 ? $"{s / 3600}h"
                : s % 60 == 0 ? $"{s / 60}m"
                : $"{s}s";
        }
    }
}
