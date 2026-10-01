using System.Text;
using Docker.DotNet.Models;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Mappers;
using DockiUp.Application.Models;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DockiUp.Infrastructure.Services
{
    public class DockerService : IDockerService
    {
        private readonly IDockiUpDockerClient _dockiUpDockerClient;
        private readonly IDockiUpDbContext _dbContext;
        private readonly SystemPaths _systemPaths;
        private readonly IDockiUpProjectConfigurationService _projectFiles;

        public DockerService(IDockiUpDockerClient dockiUpDockerClient, IDockiUpDbContext dbContext, IOptions<SystemPaths> systemPaths, IDockiUpProjectConfigurationService projectFiles)
        {
            _dockiUpDockerClient = dockiUpDockerClient;
            _dbContext = dbContext;
            _systemPaths = systemPaths.Value;
            _projectFiles = projectFiles;
        }

        public async Task<ProjectDto[]> GetRawProjectsAsync()
        {
            var containers = await _dockiUpDockerClient.DockerClient.Containers
                .ListContainersAsync(new ContainersListParameters { All = true });
            var composeFiles = ComposeFileLabels(containers);

            return containers
                .Select(container =>
                    {
                        container.Labels.TryGetValue("com.docker.compose.project", out string? projectName);
                        container.Labels.TryGetValue("com.docker.compose.service", out string? serviceName);

                        if (string.IsNullOrEmpty(projectName))
                            return (ContainerDto?)null;

                        return new ContainerDto
                        {
                            Id = container.ID,
                            Name = container.Names.SingleOrDefault()?.TrimStart('/') ?? string.Empty,
                            Status = container.Status,
                            State = container.State.ToEnum(),
                            ProjectName = projectName,
                            ServiceName = serviceName ?? string.Empty
                        };
                    }
                )
                .Where(dto => dto != null)
                .Cast<ContainerDto>()
                .GroupBy(containerDto => containerDto.ProjectName)
                .Select(group => new ProjectDto
                {
                    Id = null,
                    ProjectName = group.Key,
                    ProjectDescription = "Not Managed By DockiUp",
                    ManagedByDockiUp = false,
                    DockerProjectName = group.Key,
                    Containers = group.ToArray(),
                    ProjectPath = null,
                    UpdateMethod = null,
                    ComposeWorkingDir = composeFiles.GetValueOrDefault(group.Key).WorkingDir,
                    ComposeConfigFiles = composeFiles.GetValueOrDefault(group.Key).ConfigFiles,
                    ComposeFilesReachable = composeFiles.GetValueOrDefault(group.Key).ConfigFiles is { } files
                        && files.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).All(File.Exists),
                }).ToArray();
        }

        public async Task<ProjectDto[]> GetProjectsAsync()
        {
            var projects = await GetRawProjectsAsync();
            // Tolerate colliding docker names (two projects can normalize to the same one): last wins,
            // rather than throwing and taking down the whole listing.
            var dbProjects = _dbContext.ProjectInfo
                .AsEnumerable()
                .GroupBy(a => a.DockerProjectName)
                .ToDictionary(g => g.Key, g => g.Last());

            foreach (var project in projects)
            {
                if (dbProjects.TryGetValue(project.DockerProjectName, out var proj))
                {
                    project.Id = proj.Id;
                    project.ProjectName = proj.ProjectName;
                    project.ProjectDescription = proj.Description ?? string.Empty;
                    project.ManagedByDockiUp = true;
                    project.NodeId = proj.NodeId;
                    project.ProjectPath = proj.ProjectPath;
                    project.UpdateMethod = proj.ProjectUpdateMethod.ToString();
                }
            }
            return projects;
        }

        public async Task<ProjectDto?> GetProjectByDockerNameAsync(string dockerProjectName)
        {
            var all = await GetProjectsAsync();
            return all.SingleOrDefault(p => string.Equals(p.DockerProjectName, dockerProjectName, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<ContainerDto?> InspectContainerAsync(string containerId, CancellationToken cancellationToken = default)
        {
            var list = await _dockiUpDockerClient.DockerClient.Containers
                .ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken);
            var container = list.SingleOrDefault(c => c.ID == containerId || c.ID.StartsWith(containerId, StringComparison.Ordinal));
            if (container == null)
                return null;
            container.Labels.TryGetValue("com.docker.compose.project", out string? projectName);
            container.Labels.TryGetValue("com.docker.compose.service", out string? serviceName);
            return new ContainerDto
            {
                Id = container.ID,
                Name = container.Names.SingleOrDefault()?.TrimStart('/') ?? string.Empty,
                Status = container.Status,
                State = container.State.ToEnum(),
                ProjectName = projectName ?? string.Empty,
                ServiceName = serviceName ?? string.Empty
            };
        }

        [ExcludeFromCodeCoverage] // shells out to the `docker compose` CLI; covered by integration/E2E
        public Task StartProjectAsync(string dockerProjectName)
            => RunComposeAsync(["-p", dockerProjectName, "start"], workingDirectory: null, _ => Task.CompletedTask, CancellationToken.None);

        [ExcludeFromCodeCoverage] // shells out to the `docker compose` CLI; covered by integration/E2E
        public Task StopProjectAsync(string dockerProjectName)
            => RunComposeAsync(["-p", dockerProjectName, "stop"], workingDirectory: null, _ => Task.CompletedTask, CancellationToken.None);

        [ExcludeFromCodeCoverage] // shells out to the `docker compose` CLI; covered by integration/E2E
        public Task RestartProjectAsync(string dockerProjectName)
            => RunComposeAsync(["-p", dockerProjectName, "restart"], workingDirectory: null, _ => Task.CompletedTask, CancellationToken.None);

        [ExcludeFromCodeCoverage] // shells out to the `docker compose` CLI; covered by integration/E2E
        public Task RemoveProjectAsync(string dockerProjectName, bool removeVolumes)
            => RunComposeAsync(removeVolumes ? ["-p", dockerProjectName, "down", "--volumes"] : ["-p", dockerProjectName, "down"],
                workingDirectory: null, _ => Task.CompletedTask, CancellationToken.None);

        public Task DeleteProjectFilesAsync(string projectPath)
        {
            // Only ever inside the projects root: this deletes recursively.
            var root = Path.GetFullPath(_systemPaths.ProjectsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(projectPath);
            if (!full.StartsWith(root, StringComparison.Ordinal))
                throw new ArgumentException($"Refusing to delete '{projectPath}': it is outside the projects folder.");
            if (Directory.Exists(full))
            {
                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal); // git pack files are read-only
                Directory.Delete(full, recursive: true);
            }
            return Task.CompletedTask;
        }

        public Task<GitSyncResult> SyncRepositoryAsync(string projectPath, string? branch, string? commit, Func<string, Task> log, CancellationToken cancellationToken = default, Application.Git.GitCredentials? credentials = null)
            => _projectFiles.SyncRepositoryAsync(projectPath, branch, commit, log, credentials);

        [ExcludeFromCodeCoverage] // shells out to the `docker compose` CLI; covered by integration/E2E
        public Task ComposePullAsync(ComposeTarget target, Func<string, Task> log, CancellationToken cancellationToken = default)
            => RunComposeAsync([.. ComposeProjectArgs(target), "pull", "--ignore-buildable"], target.ProjectPath, log, cancellationToken);

        [ExcludeFromCodeCoverage] // shells out to the `docker compose` CLI; covered by integration/E2E
        public async Task<ComposeUpResult> ComposeUpAsync(ComposeTarget target, Func<string, Task> log, CancellationToken cancellationToken = default)
        {
            var project = ComposeProjectArgs(target);
            var before = await ProjectContainerIdsAsync(target.DockerProjectName, cancellationToken);

            // Pull first, while the old containers still serve traffic. Buildable services are skipped here
            // and built by `up --build`.
            await RunComposeAsync([.. project, "pull", "--ignore-buildable"], target.ProjectPath, log, cancellationToken);
            await RunComposeAsync([.. project, "up", "-d", "--build", "--remove-orphans"], target.ProjectPath, log, cancellationToken);

            var after = await ProjectContainerIdsAsync(target.DockerProjectName, cancellationToken);
            return new ComposeUpResult(!before.SetEquals(after));
        }

        private async Task<HashSet<string>> ProjectContainerIdsAsync(string dockerProjectName, CancellationToken cancellationToken)
        {
            var containers = await _dockiUpDockerClient.DockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [$"com.docker.compose.project={dockerProjectName}"] = true },
                },
            }, cancellationToken);
            return containers.Select(c => c.ID).ToHashSet();
        }

        // Runs `docker compose <args>`, streaming every output line to `log` (compose writes progress to
        // stderr). The compose CLI takes no socket from our config, so when SystemPaths.DockerSocket is set
        // DOCKER_HOST points it at the same daemon Docker.DotNet uses - otherwise the two would split-brain.
        [ExcludeFromCodeCoverage] // spawns the `docker` CLI process; covered by integration/E2E
        private async Task RunComposeAsync(string[] args, string? workingDirectory, Func<string, Task> log, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
            };
            foreach (var arg in (string[])["compose", "--ansi", "never", "--progress", "plain", .. args])
                startInfo.ArgumentList.Add(arg);
            if (!string.IsNullOrWhiteSpace(_systemPaths.DockerSocket))
                startInfo.Environment["DOCKER_HOST"] = _systemPaths.DockerSocket;

            await log("$ docker compose " + string.Join(' ', args));
            using var process = Process.Start(startInfo)!;

            // Keep the tail of the output so a failure carries compose's own explanation.
            var tail = new Queue<string>();
            var gate = new SemaphoreSlim(1, 1);
            async Task Pump(StreamReader reader)
            {
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    await gate.WaitAsync(cancellationToken);
                    try
                    {
                        tail.Enqueue(line);
                        if (tail.Count > 20) tail.Dequeue();
                        await log(line);
                    }
                    finally { gate.Release(); }
                }
            }
            await Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError));
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"docker compose {string.Join(' ', args)} failed (exit {process.ExitCode}):" + Environment.NewLine
                    + string.Join(Environment.NewLine, tail));
        }

        public Task RemoveContainerAsync(string containerId)
            => _dockiUpDockerClient.DockerClient.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true });

        public async Task RestartContainerAsync(string containerId)
        {
            await _dockiUpDockerClient.DockerClient.Containers.RestartContainerAsync(containerId, new ContainerRestartParameters { WaitBeforeKillSeconds = 10 });
        }

        public async Task StartContainerAsync(string containerId)
        {
            await _dockiUpDockerClient.DockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters());
        }

        public async Task StopContainerAsync(string containerId)
        {
            await _dockiUpDockerClient.DockerClient.Containers.StopContainerAsync(containerId, new ContainerStopParameters());
        }

        public async Task<string> GetContainerLogsAsync(string containerId, int? tail = null, CancellationToken cancellationToken = default)
        {
            var parameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Timestamps = false,
                Tail = tail?.ToString() ?? "100"
            };

#pragma warning disable CS0618 // Type or member is obsolete - we decode the multiplexed stream ourselves
            using var stream = await _dockiUpDockerClient.DockerClient.Containers.GetContainerLogsAsync(containerId, parameters, cancellationToken);
#pragma warning restore CS0618
            return DecodeDockerMultiplexedStream(stream);
        }

        #region Vault secrets -> env file (#76)
        public async Task WriteEnvFileAsync(string projectPath, string composePath, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken = default)
        {
            // Paths come from the control plane; still only ever write inside the projects root.
            var root = Path.GetFullPath(_systemPaths.ProjectsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var project = Path.GetFullPath(projectPath);
            // Adopted projects list several compose files, comma-separated; .env sits next to the first.
            var compose = Path.GetFullPath(composePath.Split(',')[0].Trim());
            var insideRoot = project.StartsWith(root, StringComparison.Ordinal)
                && compose.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.Ordinal);

            var target = Path.Combine(project, Application.Deployments.EnvFile.GeneratedFileName);
            if (secrets.Count == 0)
            {
                // Nothing to write. Adopted projects live outside the projects root; leave their folder alone.
                if (insideRoot) File.Delete(target); // no-op when missing
                return;
            }
            if (!insideRoot)
                throw new ArgumentException($"Vault secrets can only be used by projects inside DockiUp's projects folder; '{projectPath}' is outside it (an adopted project).");

            // compose reads .env from the compose file's folder by default.
            var dotEnv = Path.Combine(Path.GetDirectoryName(compose)!, ".env");
            var existing = File.Exists(dotEnv) ? await File.ReadAllTextAsync(dotEnv, cancellationToken) : null;
            var content = Application.Deployments.EnvFile.Merge(existing, secrets);

            // Owner read/write only where the OS supports it; recreate so a looser existing mode can't linger.
            File.Delete(target);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(target, options))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                await writer.WriteAsync(content.AsMemory(), cancellationToken);

            ExcludeFromGit(project);
        }

        // Keep the generated file out of `git status`/`git add -A` in a checkout, so secrets can never be committed.
        private static void ExcludeFromGit(string projectPath)
        {
            var info = Path.Combine(projectPath, ".git", "info");
            if (!Directory.Exists(Path.Combine(projectPath, ".git"))) return;
            Directory.CreateDirectory(info);
            var exclude = Path.Combine(info, "exclude");
            var line = "/" + Application.Deployments.EnvFile.GeneratedFileName;
            if (File.Exists(exclude) && File.ReadAllLines(exclude).Contains(line)) return;
            File.AppendAllText(exclude, Environment.NewLine + line + Environment.NewLine);
        }
        #endregion

        private static string DecodeDockerMultiplexedStream(Stream stream)
        {
            var sb = new StringBuilder();
            var header = new byte[8];
            var buffer = new byte[4096];

            while (true)
            {
                int headerRead = stream.Read(header, 0, 8);
                if (headerRead < 8) break;

                // Docker stream format: [0] stream type (0=stdin, 1=stdout, 2=stderr), [1-3] padding, [4-7] size (big-endian uint32)
                int payloadSize = (header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7];
                if (payloadSize <= 0) continue;

                int remaining = payloadSize;
                while (remaining > 0)
                {
                    int toRead = Math.Min(remaining, buffer.Length);
                    int read = stream.Read(buffer, 0, toRead);
                    if (read <= 0) break;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    remaining -= read;
                }
            }

            return sb.ToString();
        }

        // ---- New project flow: compose file args, adoption labels, validation ----

        /// <summary>`-p NAME -f FILE...` plus one `--env-file`: the deploy-generated one (the project's .env merged
        /// with mapped vault secrets, #76) when it exists, otherwise a .env next to the (first) compose file.
        /// Adopted projects can have several files, comma-separated as compose records them in its labels.
        /// Throws a clear message when a file isn't reachable from here (an adopted project whose folder is
        /// not mounted into DockiUp).</summary>
        public static string[] ComposeProjectArgs(ComposeTarget target)
        {
            var files = target.ComposePath.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var args = new List<string> { "-p", target.DockerProjectName };
            foreach (var file in files)
            {
                if (!File.Exists(file))
                    throw new InvalidOperationException(
                        $"The compose file {file} is not reachable from DockiUp. Mount its folder into the DockiUp container (at the same path) to deploy this project from here.");
                args.AddRange(["-f", file]);
            }
            var generated = Path.Combine(target.ProjectPath, Application.Deployments.EnvFile.GeneratedFileName);
            var envFile = File.Exists(generated) ? generated : Application.ProjectPreparer.EnvFilePath(target.ComposePath);
            if (File.Exists(envFile)) args.AddRange(["--env-file", envFile]);
            return [.. args];
        }

        // compose's own labels say where a project's files are: the same on every container of the project.
        private static Dictionary<string, (string? WorkingDir, string? ConfigFiles)> ComposeFileLabels(IEnumerable<ContainerListResponse> containers)
            => containers
                .Where(c => c.Labels.ContainsKey("com.docker.compose.project"))
                .GroupBy(c => c.Labels["com.docker.compose.project"])
                .ToDictionary(g => g.Key, g =>
                {
                    var labels = g.First().Labels;
                    labels.TryGetValue("com.docker.compose.project.working_dir", out var workingDir);
                    labels.TryGetValue("com.docker.compose.project.config_files", out var configFiles);
                    return (workingDir, configFiles);
                });

        [ExcludeFromCodeCoverage] // clones and spawns the `docker` CLI; the parsing is unit-tested (ComposeValidation)
        public async Task<ComposeValidationDto> ValidateComposeAsync(ComposeValidationRequest request, CancellationToken cancellationToken = default)
        {
            var dir = Path.Combine(Path.GetTempPath(), "dockiup-validate-" + Guid.NewGuid().ToString("N"));
            try
            {
                string composePath;
                if (!string.IsNullOrWhiteSpace(request.GitUrl))
                {
                    await _projectFiles.CloneRepositoryAsync(dir, request.GitUrl.Trim(), string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch.Trim(), request.Credentials);
                    var file = string.IsNullOrWhiteSpace(request.ComposeFile) ? Application.ProjectPreparer.DefaultRepoComposeFile : request.ComposeFile.Trim();
                    composePath = Application.ProjectPreparer.ResolveInside(dir, file, "The compose file must be inside the repository.");
                    if (!File.Exists(composePath))
                        return new ComposeValidationDto(false, [$"'{file}' was not found in the repository."], [], []);
                }
                else
                {
                    Directory.CreateDirectory(dir);
                    composePath = await _projectFiles.WriteComposeFileAsync(dir, request.Compose ?? "");
                }
                if (!string.IsNullOrEmpty(request.EnvFile))
                    await File.WriteAllTextAsync(Application.ProjectPreparer.EnvFilePath(composePath), request.EnvFile, cancellationToken);

                var (exitCode, stdout, stderr) = await RunComposeCaptureAsync(
                    [.. ComposeProjectArgs(new ComposeTarget(dir, composePath, "dockiup-validate")), "config", "--format", "json"], dir, cancellationToken);
                // Messages name the temp copy; show paths relative to the project instead.
                return Application.Compose.ComposeValidation.Parse(exitCode, stdout,
                    stderr.Replace(dir + Path.DirectorySeparatorChar, "").Replace(dir, "."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new ComposeValidationDto(false, [ex.Message], [], []);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                            File.SetAttributes(file, FileAttributes.Normal); // pack files are read-only
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch { /* best effort: it's a temp folder */ }
            }
        }

        // Like RunComposeAsync, but keeps stdout (the resolved file) apart from stderr (compose's messages).
        [ExcludeFromCodeCoverage] // spawns the `docker` CLI process
        private async Task<(int ExitCode, string Stdout, string Stderr)> RunComposeCaptureAsync(string[] args, string workingDirectory, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
            };
            foreach (var arg in (string[])["compose", "--ansi", "never", .. args])
                startInfo.ArgumentList.Add(arg);
            if (!string.IsNullOrWhiteSpace(_systemPaths.DockerSocket))
                startInfo.Environment["DOCKER_HOST"] = _systemPaths.DockerSocket;

            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await stdout, await stderr);
        }
    }
}
