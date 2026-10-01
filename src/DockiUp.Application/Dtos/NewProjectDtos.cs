using DockiUp.Domain.Enums;

namespace DockiUp.Application.Dtos
{
    /// <summary>A git repository to look into before creating a project from it (public repos for now).</summary>
    /// <summary><paramref name="GitCredentialId"/> picks stored credentials for a private repository.</summary>
    public record InspectRepositoryRequest(string GitUrl, string? Branch = null, Guid? GitCredentialId = null);

    /// <summary>A compose file found in a repository: path relative to the repo root ('/'-separated).</summary>
    public record RepositoryComposeFileDto(string Path, string Content, string[] Services);

    /// <summary>What a repository offers: its branches, the inspected branch, and the compose files on it.</summary>
    public record RepositoryInspectionDto(
        string Branch,
        string? DefaultBranch,
        string[] Branches,
        RepositoryComposeFileDto[] ComposeFiles,
        string? DefaultComposeFile);

    public record ConvertDockerRunRequest(string Command);

    /// <summary>Generated compose content, plus anything that could not be carried over.</summary>
    public record GeneratedComposeDto(string Compose, string[] Warnings);

    /// <summary>A single image to run as a one-service compose project.</summary>
    public class ImageProjectDto
    {
        public required string Image { get; set; }
        public string? Tag { get; set; }
        /// <summary>"8080:80", "127.0.0.1:8080:80/udp", ...</summary>
        public string[] Ports { get; set; } = [];
        /// <summary>"./data:/data", "named-volume:/var/lib/x:ro", ...</summary>
        public string[] Volumes { get; set; } = [];
        /// <summary>"KEY=value".</summary>
        public string[] Environment { get; set; } = [];
        public string? Restart { get; set; }
    }

    /// <summary>Compose to check with `docker compose config` on the host it will run on: either inline
    /// content, or a file in a git repository (cloned to a temp folder there). The optional .env content is
    /// placed next to the compose file, like it will be for the real project.</summary>
    public record ComposeValidationRequest(
        Guid? NodeId,
        string? Compose,
        string? EnvFile,
        string? GitUrl = null,
        string? Branch = null,
        string? ComposeFile = null,
        Guid? GitCredentialId = null,
        // Set by the server from GitCredentialId (anything a client sends is overwritten); travels to a node
        // only inside the RPC payload so it can clone a private repo there.
        Git.GitCredentials? Credentials = null);

    public record ComposeServiceDto(string Name, string? Image);

    /// <summary>Outcome of `docker compose config`: errors block the deploy; warnings (like unset optional
    /// variables) don't.</summary>
    public record ComposeValidationDto(bool Valid, string[] Errors, string[] Warnings, ComposeServiceDto[] Services);

    /// <summary>Take over a compose project that already runs on a host, in place.</summary>
    public class AdoptProjectDto
    {
        public required string DockerProjectName { get; set; }
        public Guid? NodeId { get; set; }
        public string? Description { get; set; }
        public required ProjectUpdateMethod ProjectUpdateMethod { get; set; }
        public int? PeriodicIntervalInMinutes { get; set; }
    }

    public record AdoptedProjectDto(Guid Id, string DockerProjectName);
}
