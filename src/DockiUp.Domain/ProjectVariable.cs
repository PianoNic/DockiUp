namespace DockiUp.Domain
{
    /// <summary>An environment variable of a project, written to its env file at deploy time. A secret's
    /// value is stored encrypted with the vault master key and never returned; a plain variable's is not.</summary>
    public class ProjectVariable : BaseEntity
    {
        public required Guid ProjectId { get; set; }
        public required string Name { get; set; }
        /// <summary>Free-form label the UI groups by; null: ungrouped.</summary>
        public string? Group { get; set; }
        public bool IsSecret { get; set; }
        /// <summary>The value, or for a secret the vault-encrypted value.</summary>
        public required string Value { get; set; }
    }
}
