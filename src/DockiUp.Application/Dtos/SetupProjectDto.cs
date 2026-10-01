using DockiUp.Domain.Enums;

namespace DockiUp.Application.Dtos
{
    public class SetupProjectDto
    {
        public required string ProjectName { get; set; }
        public string? Description { get; set; }

        public required ProjectOriginType ProjectOrigin { get; set; }
        public string? GitUrl { get; set; }
        /// <summary>Git: branch to track (the repo's default branch when empty).</summary>
        public string? Branch { get; set; }
        /// <summary>Git: compose file path inside the repo (default docker-compose.yml).</summary>
        public string? ComposeFile { get; set; }
        public string? Compose { get; set; }
        public string? Path { get; set; }

        /// <summary>Optional target node. When set, the project is deployed to (and managed on) that
        /// node over SignalR; when null it runs on the local control-plane host.</summary>
        public Guid? NodeId { get; set; }

        public required ProjectUpdateMethod ProjectUpdateMethod { get; set; }

        public int? PeriodicIntervalInMinutes { get; set; }

        /// <summary>Git: stored credential for a private repository (null for public repos).</summary>
        public Guid? GitCredentialId { get; set; }
    }
}
