using System.Text.Json.Serialization;

namespace DockiUp.Domain.Enums
{
    /// <summary>What DockiUp does when a newer image is published for a project's service tag:
    /// nothing (not even checking), show it (and raise ImageUpdatesFound), or redeploy the changed services.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ImageUpdatePolicy>))]
    public enum ImageUpdatePolicy { Off, Notify, Auto }
}
