namespace DockiUp.Domain
{
    /// <summary>Maps an environment variable of a project to a vault secret; written to the project's
    /// env file at deploy time.</summary>
    public class ProjectSecret : BaseEntity
    {
        public required Guid ProjectId { get; set; }
        public required string EnvName { get; set; }
        public required Guid SecretId { get; set; }
    }
}
