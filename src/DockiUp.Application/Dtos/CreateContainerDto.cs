using DockiUp.Domain.Enums;

namespace DockiUp.Application.Dtos
{
    public class CreateContainerDto
    {
        public required string Name { get; set; }
        public required string GitUrl { get; set; }
        public string? Description { get; set; }
        public required UpdateMethod UpdateMethod { get; set; }

        public int? CheckIntervals { get; set; }
    }
}
