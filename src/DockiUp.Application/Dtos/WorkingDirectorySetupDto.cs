namespace DockiUp.Application.Dtos
{
    public class WorkingDirectorySetupDto
    {
        public required string Name { get; set; }
        public required string GitUrl { get; set; }
        public string? Description { get; set; }
    }
}
