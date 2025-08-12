using DockiUp.Application.Models;

namespace DockiUp.Application.Dtos
{
    public class ComposeDto
    {
        public required string FileName { get; set; }
        public required List<DockerService> Services { get; set; }
    }
}
