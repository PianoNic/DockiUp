namespace DockiUp.Application.Dtos
{
    /// <summary>
    /// Project/stack DTO (Komodo-style: one deployable unit with containers).
    /// </summary>
    public class ProjectDto
    {
        /// <summary>Database id when managed by DockiUp.</summary>
        public Guid? Id { get; set; }
        public required string ProjectName { get; set; }
        public required string DockerProjectName { get; set; }
        public required string ProjectDescription { get; set; }
        public required bool ManagedByDockiUp { get; set; }
        /// <summary>The node this project runs on, or null for the local control-plane host.</summary>
        public Guid? NodeId { get; set; }
        public required ContainerDto[] Containers { get; set; }
        /// <summary>Project path on disk when managed.</summary>
        public string? ProjectPath { get; set; }
        /// <summary>Update method: Webhook, Manual, Periodically.</summary>
        public string? UpdateMethod { get; set; }
        /// <summary>What the last successful deployment shipped (DockiUp projects only).</summary>
        public string? DeployedCommit { get; set; }
        public string? DeployedCommitMessage { get; set; }
        public DateTime? DeployedAt { get; set; }

        // From compose's own container labels: where an existing project's files are, so it can be adopted.
        public string? ComposeWorkingDir { get; set; }
        /// <summary>Comma-separated, as compose records them.</summary>
        public string? ComposeConfigFiles { get; set; }
        /// <summary>Whether DockiUp can read those files from where it runs (needed to deploy, not to adopt).</summary>
        public bool ComposeFilesReachable { get; set; }
    }
}