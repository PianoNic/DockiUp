using DockiUp.Domain.Enums;
using DockiUp.Domain.Models;

namespace DockiUp.Application.Dtos
{
    public class ContainerDto
    {
        public int DbContainerId { get; set; }
        public string? ContainerId { get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string GitUrl { get; set; }
        public required string Path { get; set; }
        public required DateTime LastUpdated { get; set; }
        public required DateTime LastGitPush { get; set; }
        public required StatusType Status { get; set; }
        public required UpdateMethod UpdateMethod { get; set; }

        public WebhookSecret? WebhookSecret { get; set; }
        public int? CheckIntervals { get; set; }
    }
}
