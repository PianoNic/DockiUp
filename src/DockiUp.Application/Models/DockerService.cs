namespace DockiUp.Application.Models
{
    public class DockerService
    {
        public string? ServiceName { get; set; }
        public List<string>? Ports { get; set; }
        public List<string>? EnvironmentVariables { get; set; }
        public List<string>? EnvFiles { get; set; }
    }
}
