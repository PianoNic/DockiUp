using DockiUp.API.Settings;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace DockiUp.API.Controllers
{
    /// <summary>DockiUp's own settings. A setting given as an environment variable is shown locked with that value.</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController(IDockiUpDbContext db, IConfiguration configuration, DatabaseSettingsProvider provider, IActivityLogger activity) : ControllerBase
    {
        /// <param name="Value">What DockiUp uses right now (null: nothing set, <see cref="Default"/> applies).</param>
        /// <param name="LockedBy">The environment variable that sets it; the UI can't change it then.</param>
        public record SettingDto(string Key, string Group, string Label, string Hint, string Type, string? Value, string? Default, double? Min, string? LockedBy);

        /// <param name="Value">Empty or null goes back to the default.</param>
        public record SaveSettingRequest(string Key, string? Value);

        [HttpGet(Name = "ListSettings")]
        [ProducesResponseType(typeof(SettingDto[]), StatusCodes.Status200OK)]
        public ActionResult<SettingDto[]> ListSettings()
            => Ok(AppSettings.All.Select(ToDto).ToArray());

        [HttpPut(Name = "SaveSetting")]
        [ProducesResponseType(typeof(SettingDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SettingDto>> SaveSetting([FromBody] SaveSettingRequest request, CancellationToken cancellationToken)
        {
            var setting = AppSettings.All.FirstOrDefault(s => s.Key == request.Key)
                ?? throw new ArgumentException($"Unknown setting '{request.Key}'.");
            if (AppSettings.LockedBy(setting) is { } env)
                throw new ArgumentException($"{setting.Label} is set by {env}; change it there.");
            var value = Validate(setting, request.Value?.Trim());

            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == setting.Key, cancellationToken);
            if (value is null) { if (row is not null) db.AppSettings.Remove(row); }
            else if (row is null) db.AppSettings.Add(new AppSetting { Key = setting.Key, Value = value });
            else row.Value = value;
            await db.SaveChangesAsync(cancellationToken);
            provider.Set(await db.AppSettings.AsNoTracking().ToListAsync(cancellationToken));

            await activity.LogAsync("setting", setting.Label, details: value ?? "default", cancellationToken: cancellationToken);
            return Ok(ToDto(setting));
        }

        private SettingDto ToDto(SettingDefinition s) => new(s.Key, s.Group, s.Label, s.Hint, s.Type.ToString().ToLowerInvariant(),
            s.Key == "DockiUp:PublicUrl" ? AppSettings.PublicUrl(configuration) : configuration[s.Key], s.Default, s.Min, AppSettings.LockedBy(s));

        private static string? Validate(SettingDefinition s, string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            switch (s.Type)
            {
                case SettingType.Number:
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < (s.Min ?? double.MinValue))
                        throw new ArgumentException($"{s.Label} must be a number{(s.Min is { } min ? $" of at least {min.ToString(CultureInfo.InvariantCulture)}" : "")}.");
                    return n.ToString(CultureInfo.InvariantCulture);
                case SettingType.Duration:
                    var duration = DockiUp.Application.Durations.ParseInterval(value, s.Label);
                    if (s.Min is { } seconds && duration > TimeSpan.Zero && duration.TotalSeconds < seconds)
                        throw new ArgumentException($"{s.Label} must be at least {DockiUp.Application.Durations.Format(TimeSpan.FromSeconds(seconds))}, or 0 to turn it off.");
                    return DockiUp.Application.Durations.Format(duration);
                default:
                    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                        throw new ArgumentException($"{s.Label} must be an http(s) address, e.g. https://dockiup.example.com.");
                    return value.TrimEnd('/');
            }
        }
    }
}
