using System.ComponentModel.DataAnnotations;

namespace DockiUp.Domain
{
    /// <summary>A DockiUp setting changed in the UI, stored as a configuration key (e.g. "ImageUpdates:IntervalHours").
    /// Environment variables still win over it.</summary>
    public class AppSetting
    {
        [Key]
        public required string Key { get; init; }
        public required string Value { get; set; }
    }
}
