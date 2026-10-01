using DockiUp.Application.Dtos;

namespace DockiUp.Application.Interfaces
{
    /// <summary>
    /// Docker and compose operations (Komodo-style: projects/stacks and container lifecycle).
    /// </summary>
    public interface IDockerService
    {
        Task<ProjectDto[]> GetProjectsAsync();

        /// <summary>Daemon-only project listing: no database reconciliation (Id/ManagedByDockiUp left
        /// unset). Safe to run on a node, which has no application database; the control plane reconciles
        /// the returned projects against its own records.</summary>
        Task<ProjectDto[]> GetRawProjectsAsync();

        Task<ProjectDto?> GetProjectByDockerNameAsync(string dockerProjectName);
        Task<ContainerDto?> InspectContainerAsync(string containerId, CancellationToken cancellationToken = default);

        // Lifecycle by compose project name (`docker compose -p`): needs no compose files, so it works for
        // every project on the host, managed by DockiUp or not.
        Task StartProjectAsync(string dockerProjectName);
        Task StopProjectAsync(string dockerProjectName);
        Task RestartProjectAsync(string dockerProjectName);
        /// <summary>`docker compose -p NAME down` (containers + networks); volumes only when asked, they hold data.</summary>
        Task RemoveProjectAsync(string dockerProjectName, bool removeVolumes);
        /// <summary>Deletes a project folder DockiUp created under the projects root (runs where it lives).</summary>
        Task DeleteProjectFilesAsync(string projectPath);

        /// <summary>Deploy pipeline step 1 (git projects): sync the checkout. Runs where the checkout lives.</summary>
        Task<GitSyncResult> SyncRepositoryAsync(string projectPath, string? branch, string? commit, Func<string, Task> log, CancellationToken cancellationToken = default, Git.GitCredentials? credentials = null);

        /// <summary>Deploy pipeline step 2: `compose pull` then `compose up -d --build --remove-orphans`,
        /// streaming output to <paramref name="log"/>. Throws with compose's output when it fails.</summary>
        /// <summary>`compose pull` only: downloads newer images without touching running containers.</summary>
        Task ComposePullAsync(ComposeTarget target, Func<string, Task> log, CancellationToken cancellationToken = default);

        Task<ComposeUpResult> ComposeUpAsync(ComposeTarget target, Func<string, Task> log, CancellationToken cancellationToken = default);

        Task StartContainerAsync(string containerId);
        Task StopContainerAsync(string containerId);
        Task RestartContainerAsync(string containerId);
        /// <summary>Force-removes one container (compose recreates it on the next deploy).</summary>
        Task RemoveContainerAsync(string containerId);

        /// <summary>
        /// Get container logs (stdout + stderr). Returns decoded text.
        /// </summary>
        Task<string> GetContainerLogsAsync(string containerId, int? tail = null, CancellationToken cancellationToken = default);

        /// <summary>Deploy pipeline (before compose pull/up): writes the project's generated env file - its
        /// <c>.env</c> merged with <paramref name="secrets"/> (secrets win) - which compose then gets via
        /// <c>--env-file</c>. No secrets removes the generated file, so compose falls back to plain <c>.env</c>.
        /// Runs where the project lives; values are never logged.</summary>
        Task WriteEnvFileAsync(string projectPath, string composePath, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken = default);

        /// <summary>`docker compose config` on this host for a compose file that isn't a project yet
        /// (inline, or from a git repo cloned to a temp folder). Never throws for a bad file: the problems
        /// come back as errors.</summary>
        Task<ComposeValidationDto> ValidateComposeAsync(ComposeValidationRequest request, CancellationToken cancellationToken = default);
    }
}
