using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace DockiUp.API.Nodes
{
    /// <summary>An <see cref="IDockerService"/> whose calls run on a remote node. Each method invokes
    /// the matching handler on the node's live SignalR connection; the node executes it against its own
    /// Docker daemon (and its own project folders) and returns the result. Void operations map to a
    /// bool result because SignalR client-result invocations must return a value.</summary>
    public class RemoteDockerService(Guid nodeId, IHubContext<NodeHub> hub, INodeRegistry registry, DeployLogRelay logRelay) : IDockerService
    {
        private ISingleClientProxy Node()
        {
            if (!registry.TryGetConnectionId(nodeId, out var connectionId))
                throw new NodeOfflineException(nodeId);
            return hub.Clients.Client(connectionId);
        }

        // A node has no application database, so it can only ever return raw (unreconciled) projects;
        // the control plane reconciles them against its own records. Both entry points hit the same call.
        public Task<ProjectDto[]> GetProjectsAsync()
            => Node().InvokeAsync<ProjectDto[]>("GetProjects", CancellationToken.None);

        public Task<ProjectDto[]> GetRawProjectsAsync()
            => Node().InvokeAsync<ProjectDto[]>("GetProjects", CancellationToken.None);

        public Task<ProjectDto?> GetProjectByDockerNameAsync(string dockerProjectName)
            => Node().InvokeAsync<ProjectDto?>("GetProjectByDockerName", dockerProjectName, CancellationToken.None);

        public Task<ContainerDto?> InspectContainerAsync(string containerId, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<ContainerDto?>("InspectContainer", containerId, cancellationToken);

        public Task StartProjectAsync(string dockerProjectName)
            => Node().InvokeAsync<bool>("StartProject", dockerProjectName, CancellationToken.None);

        public Task StopProjectAsync(string dockerProjectName)
            => Node().InvokeAsync<bool>("StopProject", dockerProjectName, CancellationToken.None);

        public Task RestartProjectAsync(string dockerProjectName)
            => Node().InvokeAsync<bool>("RestartProject", dockerProjectName, CancellationToken.None);

        public Task RemoveProjectAsync(string dockerProjectName, bool removeVolumes)
            => Node().InvokeAsync<bool>("RemoveProject", dockerProjectName, removeVolumes, CancellationToken.None);

        public Task DeleteProjectFilesAsync(string projectPath)
            => Node().InvokeAsync<bool>("DeleteProjectFiles", projectPath, CancellationToken.None);

        public Task RemoveContainerAsync(string containerId)
            => Node().InvokeAsync<bool>("RemoveContainer", containerId, CancellationToken.None);

        // Pipeline steps run on the node; it streams output lines back via NodeHub.DeployLog(runId, line).
        // Git credentials ride in the invocation payload (never a URL or a log line).
        public Task<GitSyncResult> SyncRepositoryAsync(string projectPath, string? branch, string? commit, Func<string, Task> log, CancellationToken cancellationToken = default, DockiUp.Application.Git.GitCredentials? credentials = null)
            => WithLog(log, runId => Node().InvokeAsync<GitSyncResult>("SyncRepository", projectPath, branch, commit, runId, credentials, cancellationToken));

        public Task ComposePullAsync(ComposeTarget target, Func<string, Task> log, CancellationToken cancellationToken = default)
            => WithLog(log, runId => Node().InvokeAsync<bool>("ComposePull", target, runId, cancellationToken));

        public Task<ComposeUpResult> ComposeUpAsync(ComposeTarget target, Func<string, Task> log, CancellationToken cancellationToken = default)
            => WithLog(log, runId => Node().InvokeAsync<ComposeUpResult>("ComposeUp", target, runId, cancellationToken));

        private async Task<T> WithLog<T>(Func<string, Task> log, Func<string, Task<T>> call)
        {
            var runId = logRelay.Register(log);
            try { return await call(runId); }
            finally { logRelay.Remove(runId); }
        }

        public Task StartContainerAsync(string containerId)
            => Node().InvokeAsync<bool>("StartContainer", containerId, CancellationToken.None);

        public Task StopContainerAsync(string containerId)
            => Node().InvokeAsync<bool>("StopContainer", containerId, CancellationToken.None);

        public Task RestartContainerAsync(string containerId)
            => Node().InvokeAsync<bool>("RestartContainer", containerId, CancellationToken.None);

        public Task<string> GetContainerLogsAsync(string containerId, int? tail = null, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<string>("GetContainerLogs", containerId, tail, cancellationToken);

        // Secret values travel only inside the invocation payload; the node writes the file on its own disk.
        public Task WriteEnvFileAsync(string projectPath, string composePath, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<bool>("WriteEnvFile", projectPath, composePath, new Dictionary<string, string>(secrets), cancellationToken);

        // New project flow: validate where the project will run (the node's compose version and filesystem).
        public Task<ComposeValidationDto> ValidateComposeAsync(ComposeValidationRequest request, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<ComposeValidationDto>("ValidateCompose", request, cancellationToken);

        #region Project files (#65/#66/#67)
        public Task<ProjectFileEntryDto[]> ListProjectFilesAsync(string projectPath, string? path, CancellationToken cancellationToken = default)
            => UserFacing(() => Node().InvokeAsync<ProjectFileEntryDto[]>("ListProjectFiles", projectPath, path, cancellationToken));

        public Task<ProjectFileContentDto> ReadProjectFileAsync(string projectPath, string path, CancellationToken cancellationToken = default)
            => UserFacing(() => Node().InvokeAsync<ProjectFileContentDto>("ReadProjectFile", projectPath, path, cancellationToken));

        public Task<byte[]> DownloadProjectFileAsync(string projectPath, string path, CancellationToken cancellationToken = default)
            => UserFacing(() => Node().InvokeAsync<byte[]>("DownloadProjectFile", projectPath, path, cancellationToken));

        public Task<ProjectFileWriteResult> WriteProjectFileAsync(string projectPath, string path, byte[] content, ProjectFileCommit? commit, CancellationToken cancellationToken = default)
            => UserFacing(() => Node().InvokeAsync<ProjectFileWriteResult>("WriteProjectFile", projectPath, path, content, commit, cancellationToken));

        public Task DeleteProjectFileAsync(string projectPath, string path, CancellationToken cancellationToken = default)
            => UserFacing(() => Node().InvokeAsync<bool>("DeleteProjectFile", projectPath, path, cancellationToken));

        public Task CreateProjectFolderAsync(string projectPath, string path, CancellationToken cancellationToken = default)
            => UserFacing(() => Node().InvokeAsync<bool>("CreateProjectFolder", projectPath, path, cancellationToken));

        public Task<ComposeValidationResult> ValidateProjectComposeAsync(ComposeTarget target, string? composeOverrideContent, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<ComposeValidationResult>("ValidateProjectCompose", target, composeOverrideContent, cancellationToken);

        // A node's file errors (not found, too large, outside the folder, push rejected) arrive as a bare
        // HubException; surface their message as a 400 so the UI can show why, like the local ones.
        private static async Task<T> UserFacing<T>(Func<Task<T>> call)
        {
            try { return await call(); }
            catch (HubException ex) { throw new ArgumentException(ex.Message, ex); }
        }
        #endregion

        // ---- Image updates (#68/#70) ----
        public Task<ServiceImageDto[]> GetServiceImagesAsync(string dockerProjectName, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<ServiceImageDto[]>("GetServiceImages", dockerProjectName, cancellationToken);

        public Task<Dictionary<string, string>> GetImageOverridesAsync(string projectPath)
            => Node().InvokeAsync<Dictionary<string, string>>("GetImageOverrides", projectPath, CancellationToken.None);

        public Task SetImageOverrideAsync(string projectPath, string service, string? image)
            => Node().InvokeAsync<bool>("SetImageOverride", projectPath, service, image, CancellationToken.None);

        // ---- Monitoring and housekeeping (stats, filtered logs, images/volumes/networks, prune) ----

        // Plain options go through the original handler, so a node on an older image keeps serving logs.
        public Task<string> GetContainerLogsAsync(string containerId, ContainerLogOptions options, CancellationToken cancellationToken = default)
            => options is { Stdout: true, Stderr: true, Timestamps: false }
                ? Node().InvokeAsync<string>("GetContainerLogs", containerId, options.Tail, cancellationToken)
                : Node().InvokeAsync<string>("GetContainerLogsWithOptions", containerId, options, cancellationToken);

        public async Task<ContainerStatsDto[]> GetContainerStatsAsync(CancellationToken cancellationToken = default)
            => (await Node().InvokeAsync<ContainerStatsDto[]>("GetContainerStats", cancellationToken))
                .Select(s => s with { NodeId = nodeId }).ToArray();

        public Task<DockerResourcesDto> GetResourcesAsync(CancellationToken cancellationToken = default)
            => Node().InvokeAsync<DockerResourcesDto>("GetResources", cancellationToken);

        public Task RemoveResourceAsync(ResourceKind kind, string id, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<bool>("RemoveResource", kind, id, cancellationToken);

        public Task<PruneResultDto> PruneAsync(PruneRequest request, CancellationToken cancellationToken = default)
            => Node().InvokeAsync<PruneResultDto>("PruneResources", request, cancellationToken);
    }
}
