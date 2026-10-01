using System.Text.Json.Serialization;

namespace DockiUp.Domain.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter<CleanupFrequency>))]
    public enum CleanupFrequency { Off, Daily, Weekly }
}
