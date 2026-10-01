using DockiUp.Domain.Enums;

namespace DockiUp.Domain
{
    public class ProjectInfo : BaseEntity
    {
        public required string ProjectName { get; set; }
        public required string DockerProjectName { get; set; }
        public string? Description { get; set; }

        public required ProjectOriginType ProjectOrigin { get; set; }
        public string? GitUrl { get; set; }

        /// <summary>The node this project runs on, or null for the local control-plane host.
        /// Operations on the project are routed to this node over SignalR.</summary>
        public Guid? NodeId { get; set; }
        public required string ProjectPath { get; set; }
        public required string ComposePath { get; set; }

        public required ProjectUpdateMethod ProjectUpdateMethod { get; set; }
        /// <summary>Git branch the checkout tracks; recorded at clone time when not chosen explicitly.</summary>
        public string? Branch { get; set; }
        /// <summary>Per-project webhook secret: accepted as a GitHub/Gitea HMAC key, GitLab token, or X-Webhook-Secret.</summary>
        public string WebhookSecret { get; set; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        public int? PeriodicIntervalInMinutes { get; set; }
        /// <summary>Last time periodic update ran (Komodo-style polling).</summary>
        public DateTime? LastPeriodicUpdateAt { get; set; }

        /// <summary>Credentials for cloning/fetching a private repository (git projects).</summary>
        public Guid? GitCredentialId { get; set; }

        /// <summary>What to do when a newer image is published for one of the project's services.</summary>
        public ImageUpdatePolicy ImageUpdatePolicy { get; set; } = ImageUpdatePolicy.Notify;
        /// <summary>Services left out of image update checks (and so never auto-updated).</summary>
        public List<string> ImageUpdateExcludedServices { get; set; } = [];
    }
}
