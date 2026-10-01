using System.Runtime.InteropServices;
using DockiUp.Application.Dtos;
using DockiUp.Application.Git;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Domain.Enums;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace DockiUp.API.Nodes
{
    /// <summary>Runs only when this process boots in the <c>node</c> role. It dials OUT to the control
    /// plane's <c>/hubs/node</c> over SignalR (NAT-friendly), registers itself, and answers control-plane
    /// invocations. Booting never blocks on the control plane being up - the connection retries.
    /// Exercised end-to-end by the multi-server integration tests (a live SignalR connection + daemon are
    /// required), so it's excluded from unit-coverage rather than mocked line-by-line.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class NodeAgentHostedService(
        IConfiguration configuration,
        IServiceProvider services,
        ILogger<NodeAgentHostedService> logger) : IHostedService, IAsyncDisposable
    {
        private HubConnection? _connection;
        private readonly CancellationTokenSource _shutdown = new();
        private string _nodeId = "";
        // Interactive exec sessions this node runs on the control plane's behalf, keyed by the
        // control-plane session id so input/resize/end can find them.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IContainerExecSession> _execSessions = new();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var serverUrl = configuration["Node:ServerUrl"];
            var token = configuration["Node:Token"];

            if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(token))
            {
                logger.LogError("Node role is active but Node:ServerUrl and/or Node:Token are not set. The agent will not connect.");
                return Task.CompletedTask;
            }

            // Nodes added in the UI are identified by their token on the server, which ignores this id.
            // Only legacy Node:Tokens allow-list nodes rely on a self-reported, stable Node:Id.
            if (!Guid.TryParse(configuration["Node:Id"], out var nodeId))
            {
                nodeId = Guid.NewGuid();
                logger.LogDebug("Node:Id not set; reporting {NodeId} (only used by legacy Node:Tokens nodes).", nodeId);
            }
            _nodeId = nodeId.ToString();

            var name = configuration["Node:Name"];
            if (string.IsNullOrWhiteSpace(name)) name = Environment.MachineName;

            var hubUrl = $"{serverUrl.TrimEnd('/')}/hubs/node?access_token={Uri.EscapeDataString(token)}";

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Server -> node calls. Ping is the phase-1 channel proof.
            _connection.On("Ping", () => "pong");
            RegisterDockerHandlers(_connection);
            RegisterProjectHandlers(_connection);
            RegisterExecHandlers(_connection);

            // Re-register after every (re)connect, since the registry is keyed by connection id and a
            // reconnect yields a fresh one.
            _connection.Reconnected += async _ => await RegisterAsync(name, CancellationToken.None);

            // Kick off the connect loop in the background so app startup isn't blocked on the control plane.
            _ = ConnectLoopAsync(name, serverUrl);
            _ = HeartbeatLoopAsync(_shutdown.Token);
            return Task.CompletedTask;
        }

        // Liveness: every 5s the node pings the control plane (which refreshes its "last seen").
        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    if (_connection is { State: HubConnectionState.Connected } connection)
                    {
                        try { await connection.SendAsync("Heartbeat", cancellationToken); }
                        catch (Exception ex) { logger.LogDebug("Node heartbeat failed: {Message}", ex.Message); }
                    }
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
        }

        // Takes the bare server URL for logging only: the hub URL carries the node token and must never be logged.
        private async Task ConnectLoopAsync(string name, string serverUrl)
        {
            var delay = TimeSpan.FromSeconds(2);
            while (_connection is not null)
            {
                try
                {
                    await _connection.StartAsync();
                    logger.LogInformation("Node agent connected to server at {Url}.", serverUrl);
                    await RegisterAsync(name, CancellationToken.None);
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Node agent could not reach the server at {Url} ({Message}); retrying in {Delay}s.", serverUrl, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay);
                    delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
                }
            }
        }

        private async Task RegisterAsync(string name, CancellationToken cancellationToken)
        {
            if (_connection is null) return;

            var dockerVersion = "unknown";
            try
            {
                using var scope = services.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<IDockiUpDockerClient>();
                var version = await client.DockerClient.System.GetVersionAsync(cancellationToken);
                dockerVersion = version.Version;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Node agent could not read the local Docker version: {Message}", ex.Message);
            }

            var registration = new NodeRegistrationDto(
                Id: _nodeId,
                Name: name,
                MachineName: Environment.MachineName,
                Os: RuntimeInformation.OSDescription,
                DockerVersion: dockerVersion);

            try
            {
                await _connection.InvokeAsync("Register", registration, cancellationToken);
                logger.LogInformation("Node agent registered as {Name}.", name);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Node agent failed to register: {Message}", ex.Message);
            }
        }

        // The control plane invokes these on the node's connection (RemoteDockerService); each runs
        // against the node's local Docker daemon and returns the result. Void Docker ops return a bool
        // because SignalR client-result invocations must return a value.
        private void RegisterDockerHandlers(HubConnection c)
        {
            // Raw (no DB reconciliation): the node has no application database; the control plane reconciles.
            c.On("GetProjects", () => WithDocker(d => d.GetRawProjectsAsync()));
            c.On<string, ProjectDto?>("GetProjectByDockerName", name => WithDocker(async d =>
                (await d.GetRawProjectsAsync()).FirstOrDefault(p =>
                    string.Equals(p.DockerProjectName, name, StringComparison.OrdinalIgnoreCase))));
            c.On<string, ContainerDto?>("InspectContainer", id => WithDocker(d => d.InspectContainerAsync(id)));
            c.On<string, bool>("StartProject", name => WithDocker(async d => { await d.StartProjectAsync(name); return true; }));
            c.On<string, bool>("StopProject", name => WithDocker(async d => { await d.StopProjectAsync(name); return true; }));
            c.On<string, bool>("RestartProject", name => WithDocker(async d => { await d.RestartProjectAsync(name); return true; }));
            c.On<string, bool, bool>("RemoveProject", (name, volumes) => WithDocker(async d => { await d.RemoveProjectAsync(name, volumes); return true; }));
            c.On<string, bool>("DeleteProjectFiles", path => WithDocker(async d => { await d.DeleteProjectFilesAsync(path); return true; }));
            c.On<string, bool>("RemoveContainer", id => WithDocker(async d => { await d.RemoveContainerAsync(id); return true; }));
            // Deploy pipeline steps: output lines stream back to the server as DeployLog(runId, line).
            c.On<string, string?, string?, string, GitCredentials?, GitSyncResult>("SyncRepository", (path, branch, commit, runId, credentials) =>
                WithDocker(d => d.SyncRepositoryAsync(path, branch, commit, line => c.SendAsync("DeployLog", runId, line), credentials: credentials)));
            c.On<ComposeTarget, string, bool>("ComposePull", (target, runId) =>
                WithDocker(async d => { await d.ComposePullAsync(target, line => c.SendAsync("DeployLog", runId, line)); return true; }));
            c.On<ComposeTarget, string, ComposeUpResult>("ComposeUp", (target, runId) =>
                WithDocker(d => d.ComposeUpAsync(target, line => c.SendAsync("DeployLog", runId, line))));
            c.On<string, bool>("StartContainer", id => WithDocker(async d => { await d.StartContainerAsync(id); return true; }));
            c.On<string, bool>("StopContainer", id => WithDocker(async d => { await d.StopContainerAsync(id); return true; }));
            c.On<string, bool>("RestartContainer", id => WithDocker(async d => { await d.RestartContainerAsync(id); return true; }));
            c.On<string, int?, string>("GetContainerLogs", (id, tail) => WithDocker(d => d.GetContainerLogsAsync(id, tail)));
            c.On<string, string, Dictionary<string, string>, bool>("WriteEnvFile", (path, composePath, secrets) =>
                WithDocker(async d => { await d.WriteEnvFileAsync(path, composePath, secrets); return true; }));

            // New project flow
            c.On<ComposeValidationRequest, ComposeValidationDto>("ValidateCompose", request => WithDocker(d => d.ValidateComposeAsync(request)));


            // Project files: confined to this node's projects folder by DockerService itself.
            c.On<string, string?, ProjectFileEntryDto[]>("ListProjectFiles", (root, path) => WithDocker(d => d.ListProjectFilesAsync(root, path)));
            c.On<string, string, ProjectFileContentDto>("ReadProjectFile", (root, path) => WithDocker(d => d.ReadProjectFileAsync(root, path)));
            c.On<string, string, byte[]>("DownloadProjectFile", (root, path) => WithDocker(d => d.DownloadProjectFileAsync(root, path)));
            c.On<string, string, byte[], ProjectFileCommit?, ProjectFileWriteResult>("WriteProjectFile", (root, path, content, commit) =>
                WithDocker(d => d.WriteProjectFileAsync(root, path, content, commit)));
            c.On<string, string, bool>("DeleteProjectFile", (root, path) => WithDocker(async d => { await d.DeleteProjectFileAsync(root, path); return true; }));
            c.On<string, string, bool>("CreateProjectFolder", (root, path) => WithDocker(async d => { await d.CreateProjectFolderAsync(root, path); return true; }));
            c.On<ComposeTarget, string?, ComposeValidationResult>("ValidateProjectCompose", (target, content) => WithDocker(d => d.ValidateProjectComposeAsync(target, content)));

            // Image updates (#68/#70): service images for the registry check, and the tag override file.
            c.On<string, ServiceImageDto[]>("GetServiceImages", name => WithDocker(d => d.GetServiceImagesAsync(name)));
            c.On<string, Dictionary<string, string>>("GetImageOverrides", path => WithDocker(d => d.GetImageOverridesAsync(path)));
            c.On<string, string, string?, bool>("SetImageOverride", (path, service, image) =>
                WithDocker(async d => { await d.SetImageOverrideAsync(path, service, image); return true; }));

            // Monitoring and housekeeping (stats, filtered logs, images/volumes/networks, prune).
            c.On<string, ContainerLogOptions, string>("GetContainerLogsWithOptions", (id, options) => WithDocker(d => d.GetContainerLogsAsync(id, options)));
            c.On("GetContainerStats", () => WithDocker(d => d.GetContainerStatsAsync()));
            c.On("GetResources", () => WithDocker(d => d.GetResourcesAsync()));
            c.On<ResourceKind, string, bool>("RemoveResource", (kind, id) => WithDocker(async d => { await d.RemoveResourceAsync(kind, id); return true; }));
            c.On<PruneRequest, PruneResultDto>("PruneResources", request => WithDocker(d => d.PruneAsync(request)));
        }

        // Deploy + git-pull run against the node's own filesystem (no app database here), so the node
        // clones/writes/composes locally and reports the paths it used back to the control plane.
        private void RegisterProjectHandlers(HubConnection c)
        {
            c.On<SetupProjectDto, GitCredentials?, PreparedProject>("PrepareProject", PrepareLocallyAsync);
        }

        // Clone / write the project's files on this node; the server then runs the deploy pipeline here.
        private async Task<PreparedProject> PrepareLocallyAsync(SetupProjectDto dto, GitCredentials? credentials)
        {
            using var scope = services.CreateScope();
            var paths = scope.ServiceProvider.GetRequiredService<IOptions<SystemPaths>>().Value;
            var files = scope.ServiceProvider.GetRequiredService<IDockiUpProjectConfigurationService>();
            return await DockiUp.Application.ProjectPreparer.PrepareAsync(dto, paths.ProjectsPath, files, credentials);
        }

        // Interactive console: run the exec on this node's daemon and bridge its TTY over the connection.
        // Output/Exited are pushed to the control plane keyed by the control-plane session id; the control
        // plane relays them to the browser. Input/resize/end come back the same way.
        private void RegisterExecHandlers(HubConnection c)
        {
            c.On<string, string, uint, uint, bool>("StartExec", async (sessionId, containerId, cols, rows) =>
            {
                var registry = services.GetRequiredService<IContainerExecRegistry>();
                var session = await registry.StartAsync(containerId, cols, rows);
                _execSessions[sessionId] = session;
                session.Output += async data =>
                {
                    if (_connection is null) return;
                    try { await _connection.SendAsync("ExecOutput", sessionId, Convert.ToBase64String(data.Span)); } catch { }
                };
                session.Exited += async code =>
                {
                    _execSessions.TryRemove(sessionId, out _);
                    if (_connection is null) return;
                    try { await _connection.SendAsync("ExecExited", sessionId, code); } catch { }
                };
                return true;
            });
            c.On<string, string, bool>("WriteExec", async (sessionId, base64) =>
            {
                if (_execSessions.TryGetValue(sessionId, out var session))
                    await session.WriteAsync(Convert.FromBase64String(base64));
                return true;
            });
            c.On<string, uint, uint, bool>("ResizeExec", async (sessionId, cols, rows) =>
            {
                if (_execSessions.TryGetValue(sessionId, out var session))
                    await session.ResizeAsync(cols, rows);
                return true;
            });
            c.On<string, bool>("EndExec", async (sessionId) =>
            {
                if (_execSessions.TryRemove(sessionId, out var session))
                    await services.GetRequiredService<IContainerExecRegistry>().EndAsync(session.Id);
                return true;
            });
        }

        private async Task<T> WithDocker<T>(Func<IDockerService, Task<T>> work)
        {
            using var scope = services.CreateScope();
            return await work(scope.ServiceProvider.GetRequiredService<IDockerService>());
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _shutdown.CancelAsync();
            if (_connection is not null)
                await _connection.StopAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
