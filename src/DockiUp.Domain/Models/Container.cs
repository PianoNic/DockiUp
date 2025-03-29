namespace DockiUp.Domain.Models
{
    public class Container
    {
        public int Id { get; set; }
        public required string ContainerId { get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string GitUrl { get; set; }
        public required string Path { get; set; }
        public required DateTime LastUpdated { get; set; }
        public required DateTime LastGitPush { get; set; }

        public WebhookSecret? WebhookSecret { get; set; }
        public int? CheckIntervals { get; set; }
    }
}
