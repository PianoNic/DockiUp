namespace DockiUp.Application.Dtos
{
    public class ComposeDto
    {
        public string? ServiceName { get; set; }
        public List<string>? Ports { get; set; }
        public List<string>? EnvironmentVariables { get; set; }
    }
}
