using System.Text;
using Docker.DotNet.Models;
using DockiUp.Application.Dtos;
using DockiUp.Application.ImageUpdates;
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
            => RunComposeAsync([.. ComposeProjectArgs(target), "pull", "--ignore-buildable", .. target.Services ?? []], target.ProjectPath, log, cancellationToken);

        [ExcludeFromCodeCoverage] // shells out to the `docker compose` CLI; covered by integration/E2E
        public async Task<ComposeUpResult> ComposeUpAsync(ComposeTarget target, Func<string, Task> log, CancellationToken cancellationToken = default)
        {
            // Includes dockiup.override.yml (tags chosen in the UI) when present; Services narrows to those.
            string[] project = ComposeProjectArgs(target);
            string[] services = target.Services ?? [];
            var before = await ProjectContainerIdsAsync(target.DockerProjectName, cancellationToken);

            // Pull first, while the old containers still serve traffic. Buildable services are skipped here
            // and built by `up --build`.
            await RunComposeAsync([.. project, "pull", "--ignore-buildable", .. services], target.ProjectPath, log, cancellationToken);
            await RunComposeAsync([.. project, "up", "-d", "--build", "--remove-orphans", .. services], target.ProjectPath, log, cancellationToken);

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

        public Task<string> GetContainerLogsAsync(string containerId, int? tail = null, CancellationToken cancellationToken = default)
            => GetContainerLogsAsync(containerId, new ContainerLogOptions(tail), cancellationToken);

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

        #region Project files (#65/#66/#67)
        // The project folder itself comes from the control plane (or a node's caller); it must be one of ours
        // before any relative path is resolved inside it.
        private string ProjectRoot(string projectPath)
        {
            var root = Path.GetFullPath(_systemPaths.ProjectsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(projectPath);
            if (!full.StartsWith(root, StringComparison.Ordinal))
                throw new ArgumentException($"'{projectPath}' is outside the projects folder.");
            if (!Directory.Exists(full))
                throw new KeyNotFoundException("The project folder does not exist on its host.");
            return full;
        }

        public Task<ProjectFileEntryDto[]> ListProjectFilesAsync(string projectPath, string? path, CancellationToken cancellationToken = default)
        {
            var root = ProjectRoot(projectPath);
            return Task.FromResult(ProjectFileSystem.List(root, path, _projectFiles.GetTrackedFiles(root)));
        }

        public Task<ProjectFileContentDto> ReadProjectFileAsync(string projectPath, string path, CancellationToken cancellationToken = default)
        {
            var root = ProjectRoot(projectPath);
            return ProjectFileSystem.ReadTextAsync(root, path, _projectFiles.GetTrackedFiles(root), cancellationToken);
        }

        public Task<byte[]> DownloadProjectFileAsync(string projectPath, string path, CancellationToken cancellationToken = default)
            => ProjectFileSystem.ReadBytesAsync(ProjectRoot(projectPath), path, cancellationToken);

        public async Task<ProjectFileWriteResult> WriteProjectFileAsync(string projectPath, string path, byte[] content, ProjectFileCommit? commit, CancellationToken cancellationToken = default)
        {
            var root = ProjectRoot(projectPath);
            var (full, previous) = await ProjectFileSystem.WriteAsync(root, path, content, cancellationToken);
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            if (!_projectFiles.GetTrackedFiles(root).Contains(relative))
                return new ProjectFileWriteResult(false, null);
            if (commit is null)
                return new ProjectFileWriteResult(true, null);
            try
            {
                return new ProjectFileWriteResult(true, await _projectFiles.CommitAndPushFileAsync(root, relative, commit.Message, commit.AuthorName, commit.Credentials));
            }
            catch
            {
                // Not pushed means the next sync would silently drop the edit; better to not keep it at all.
                await ProjectFileSystem.RestoreAsync(full, previous);
                throw;
            }
        }

        public Task DeleteProjectFileAsync(string projectPath, string path, CancellationToken cancellationToken = default)
        {
            var root = ProjectRoot(projectPath);
            var relative = Path.GetRelativePath(root, ProjectFileSystem.Resolve(root, path)).Replace('\\', '/');
            // The next sync's hard reset would bring a tracked file back; deleting it belongs in the repository.
            if (_projectFiles.GetTrackedFiles(root).Any(t => t == relative || t.StartsWith(relative + "/", StringComparison.Ordinal)))
                throw new ArgumentException($"'{path}' is tracked in git; delete it in the repository instead.");
            ProjectFileSystem.Delete(root, path);
            return Task.CompletedTask;
        }

        public Task CreateProjectFolderAsync(string projectPath, string path, CancellationToken cancellationToken = default)
        {
            ProjectFileSystem.CreateFolder(ProjectRoot(projectPath), path);
            return Task.CompletedTask;
        }

        [ExcludeFromCodeCoverage] // spawns the `docker` CLI process; covered by integration/E2E
        public async Task<ComposeValidationResult> ValidateProjectComposeAsync(ComposeTarget target, string? composeOverrideContent, CancellationToken cancellationToken = default)
        {
            var composeFile = target.ComposePath;
            string? temp = null;
            if (composeOverrideContent is not null)
            {
                // Next to the real file so relative paths (env_file, build contexts, bind mounts) resolve the same.
                temp = Path.Combine(Path.GetDirectoryName(target.ComposePath)!, $".dockiup-validate-{Guid.NewGuid():N}.yml");
                await File.WriteAllTextAsync(temp, composeOverrideContent, cancellationToken);
                composeFile = temp;
            }
            try
            {
                var startInfo = new ProcessStartInfo("docker")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = target.ProjectPath,
                };
                foreach (var arg in (string[])["compose", .. ComposeProjectArgs(target with { ComposePath = composeFile }), "config", "--services"])
                    startInfo.ArgumentList.Add(arg);
                if (!string.IsNullOrWhiteSpace(_systemPaths.DockerSocket))
                    startInfo.Environment["DOCKER_HOST"] = _systemPaths.DockerSocket;

                using var process = Process.Start(startInfo)!;
                var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                // The temp file name means nothing to the user; show the real one in errors.
                var errors = Lines((await stderr).Replace(composeFile, Path.GetFileName(target.ComposePath)));
                return process.ExitCode == 0
                    ? new ComposeValidationResult(true, [], Lines(await stdout))
                    : new ComposeValidationResult(false, errors.Length > 0 ? errors : [$"docker compose config failed (exit {process.ExitCode})."], []);
            }
            finally
            {
                if (temp is not null) File.Delete(temp);
            }
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

        /// <summary>The one place compose arguments are built, for deploy, pull and validation alike:
        /// `-p NAME -f FILE...`, then `-f dockiup.override.yml` when tags are pinned, plus one `--env-file`: the deploy-generated one (the project's .env merged
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
            // Tags pinned in the UI (#70) layer over the project's own compose file(s).
            var overridePath = Path.Combine(target.ProjectPath, ComposeOverrideFile.FileName);
            if (File.Exists(overridePath)) args.AddRange(["-f", overridePath]);
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

        // ---- Image updates (#68/#70) ----

        public async Task<ServiceImageDto[]> GetServiceImagesAsync(string dockerProjectName, CancellationToken cancellationToken = default)
        {
            var containers = await _dockiUpDockerClient.DockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [$"com.docker.compose.project={dockerProjectName}"] = true },
                },
            }, cancellationToken);

            var result = new List<ServiceImageDto>();
            foreach (var group in containers.GroupBy(c => c.Labels.TryGetValue("com.docker.compose.service", out var s) ? s : null))
            {
                if (group.Key is null) continue;
                var container = group.First();
                // The inspected config keeps the reference as written in compose; the listing shows the image
                // id instead once the tag has moved on to a newer local image.
                var inspected = await _dockiUpDockerClient.DockerClient.Containers.InspectContainerAsync(container.ID, cancellationToken);
                string[] digests = [];
                try
                {
                    var image = await _dockiUpDockerClient.DockerClient.Images.InspectImageAsync(inspected.Image, cancellationToken);
                    digests = image.RepoDigests?.ToArray() ?? [];
                }
                catch (Docker.DotNet.DockerImageNotFoundException) { /* image removed under the container */ }
                result.Add(new ServiceImageDto(group.Key, inspected.Config?.Image ?? container.Image, digests));
            }
            return result.ToArray();
        }

        public async Task<Dictionary<string, string>> GetImageOverridesAsync(string projectPath)
        {
            var file = OverridePath(projectPath);
            return ComposeOverrideFile.ReadImages(File.Exists(file) ? await File.ReadAllTextAsync(file) : null);
        }

        public async Task SetImageOverrideAsync(string projectPath, string service, string? image)
        {
            var file = OverridePath(projectPath);
            var content = ComposeOverrideFile.SetImage(File.Exists(file) ? await File.ReadAllTextAsync(file) : null, service, image);
            if (content is null) File.Delete(file);
            else await File.WriteAllTextAsync(file, content, Encoding.UTF8);
        }

        // The override lives in the project's own folder, which must be inside the projects root.
        private string OverridePath(string projectPath)
        {
            var root = Path.GetFullPath(_systemPaths.ProjectsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(projectPath);
            if (!full.StartsWith(root, StringComparison.Ordinal) || !Directory.Exists(full))
                throw new ArgumentException($"'{projectPath}' is not a project folder.");
            return Path.Combine(full, ComposeOverrideFile.FileName);
        }

        // ---- Monitoring and housekeeping (stats, filtered logs, images/volumes/networks, prune) ----

        public async Task<string> GetContainerLogsAsync(string containerId, ContainerLogOptions options, CancellationToken cancellationToken = default)
        {
            // The daemon rejects a request for neither stream; that is simply an empty log.
            if (!options.Stdout && !options.Stderr)
                return string.Empty;

#pragma warning disable CS0618 // Type or member is obsolete - we decode the multiplexed stream ourselves
            using var stream = await _dockiUpDockerClient.DockerClient.Containers.GetContainerLogsAsync(containerId, BuildLogParameters(options), cancellationToken);
#pragma warning restore CS0618
            return DecodeDockerMultiplexedStream(stream);
        }

        /// <summary>Tail null keeps the historical default of 100 lines; 0 or less reads the whole log.</summary>
        public static ContainerLogsParameters BuildLogParameters(ContainerLogOptions options) => new()
        {
            ShowStdout = options.Stdout,
            ShowStderr = options.Stderr,
            Timestamps = options.Timestamps,
            Tail = options.Tail is null ? "100" : options.Tail <= 0 ? "all" : options.Tail.Value.ToString(),
        };

        public async Task<ContainerStatsDto[]> GetContainerStatsAsync(CancellationToken cancellationToken = default)
        {
            var docker = _dockiUpDockerClient.DockerClient;
            var running = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = false }, cancellationToken);
            // Stream=false makes the daemon wait for a second reading (~1s) so precpu is filled and CPU % is
            // computable; run them side by side, a few at a time.
            using var gate = new SemaphoreSlim(8);
            var samples = await Task.WhenAll(running.Select(async container =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var capture = new StatsCapture();
                    await docker.Containers.GetContainerStatsAsync(container.ID, new ContainerStatsParameters { Stream = false }, capture, cancellationToken);
                    if (capture.Value is null) return null;
                    string? project = null;
                    container.Labels?.TryGetValue("com.docker.compose.project", out project);
                    var name = container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID;
                    return DockerStatsMath.ToDto(container.ID, name, project ?? string.Empty, capture.Value, DateTime.UtcNow);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    return null; // stopped between listing and sampling
                }
                finally { gate.Release(); }
            }));
            return samples.OfType<ContainerStatsDto>().ToArray();
        }

        // Progress<T> would post the report to the thread pool, possibly after the call returned; this keeps it synchronous.
        private sealed class StatsCapture : IProgress<ContainerStatsResponse>
        {
            public ContainerStatsResponse? Value { get; private set; }
            public void Report(ContainerStatsResponse value) => Value = value;
        }

        private static readonly HashSet<string> BuiltinNetworks = ["bridge", "host", "none"];

        public async Task<DockerResourcesDto> GetResourcesAsync(CancellationToken cancellationToken = default)
        {
            var docker = _dockiUpDockerClient.DockerClient;
            var containersTask = docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken);
            var imagesTask = docker.Images.ListImagesAsync(new ImagesListParameters { All = false }, cancellationToken);
            var volumesTask = docker.Volumes.ListAsync(cancellationToken);
            var networksTask = docker.Networks.ListNetworksAsync(new NetworksListParameters(), cancellationToken);
            var diskTask = ReadDiskUsageAsync(cancellationToken);
            await Task.WhenAll(containersTask, imagesTask, volumesTask, networksTask, diskTask);

            // The list endpoints don't say what uses what, so count it from the containers (running or stopped:
            // either keeps an image/volume/network from being pruned).
            var containers = containersTask.Result;
            var imageUse = containers.Where(c => c.ImageID is not null).GroupBy(c => c.ImageID).ToDictionary(g => g.Key, g => g.Count());
            var volumeUse = containers
                .SelectMany(c => (c.Mounts ?? []).Where(m => m.Type == "volume" && m.Name is not null).Select(m => (m.Name, c.ID)))
                .Distinct().GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.Count());
            var networkUse = containers
                .SelectMany(c => (c.NetworkSettings?.Networks?.Values ?? []).Select(n => n.NetworkID))
                .Where(id => !string.IsNullOrEmpty(id))
                .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
            var (disk, volumeSizes) = diskTask.Result;

            var images = imagesTask.Result.Select(i =>
            {
                var tags = (i.RepoTags ?? []).Where(t => t != "<none>:<none>").ToArray();
                var used = imageUse.GetValueOrDefault(i.ID);
                return new DockerImageDto(i.ID, tags, i.Size, i.Created.ToUniversalTime(), used, used > 0, tags.Length == 0);
            }).OrderByDescending(i => i.Created).ToArray();

            var volumes = (volumesTask.Result.Volumes ?? []).Select(v =>
            {
                var used = volumeUse.GetValueOrDefault(v.Name);
                DateTime? created = DateTime.TryParse(v.CreatedAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var c) ? c : null;
                long? size = volumeSizes.TryGetValue(v.Name, out var s) ? s : v.UsageData is { Size: >= 0 } usage ? usage.Size : null;
                return new DockerVolumeDto(v.Name, v.Driver, size, created, used, used > 0);
            }).OrderBy(v => v.Name).ToArray();

            var networks = networksTask.Result.Select(n =>
            {
                var used = networkUse.GetValueOrDefault(n.ID);
                return new DockerNetworkDto(n.ID, n.Name, n.Driver, n.Scope, used, used > 0, BuiltinNetworks.Contains(n.Name));
            }).OrderBy(n => n.Name).ToArray();

            return new DockerResourcesDto(images, volumes, networks, disk);
        }

        // Volume sizes and the disk summary are only exposed by /system/df, which Docker.DotNet doesn't wrap;
        // the CLI is already required for compose. Best effort: without it the page still lists everything.
        [ExcludeFromCodeCoverage] // spawns the `docker` CLI process
        private async Task<(DiskUsageDto, Dictionary<string, long>)> ReadDiskUsageAsync(CancellationToken cancellationToken)
        {
            try
            {
                var summary = RunDockerCliAsync(["system", "df", "--format", "json"], cancellationToken);
                var verbose = RunDockerCliAsync(["system", "df", "-v", "--format", "json"], cancellationToken);
                await Task.WhenAll(summary, verbose);
                return (DockerStatsMath.ParseDiskUsage(summary.Result), DockerStatsMath.ParseVolumeSizes(verbose.Result));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return (new DiskUsageDto(null, null, null, null, null, null), []);
            }
        }

        [ExcludeFromCodeCoverage] // spawns the `docker` CLI process
        private async Task<string> RunDockerCliAsync(string[] args, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args) startInfo.ArgumentList.Add(arg);
            if (!string.IsNullOrWhiteSpace(_systemPaths.DockerSocket))
                startInfo.Environment["DOCKER_HOST"] = _systemPaths.DockerSocket;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"docker {string.Join(' ', args)} failed: {await stderr}");
            return await stdout;
        }

        public async Task RemoveResourceAsync(ResourceKind kind, string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A resource id is required.");
            var docker = _dockiUpDockerClient.DockerClient;
            try
            {
                switch (kind)
                {
                    case ResourceKind.Image:
                        await docker.Images.DeleteImageAsync(id, new ImageDeleteParameters { Force = false }, cancellationToken);
                        break;
                    case ResourceKind.Volume:
                        await docker.Volumes.RemoveAsync(id, false, cancellationToken);
                        break;
                    case ResourceKind.Network:
                        await docker.Networks.DeleteNetworkAsync(id, cancellationToken);
                        break;
                }
            }
            catch (Docker.DotNet.DockerApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.Forbidden)
            {
                // "in use" and similar refusals are the user's to fix; pass the daemon's reason on as a 400.
                throw new ArgumentException(DockerErrorMessage(ex), ex);
            }
            catch (Docker.DotNet.DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new KeyNotFoundException($"{kind} '{id}' no longer exists.", ex);
            }
        }

        private static string DockerErrorMessage(Docker.DotNet.DockerApiException ex)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(ex.ResponseBody);
                if (doc.RootElement.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } text)
                    return text;
            }
            catch (System.Text.Json.JsonException) { }
            return ex.Message;
        }

        public async Task<PruneResultDto> PruneAsync(PruneRequest request, CancellationToken cancellationToken = default)
        {
            var docker = _dockiUpDockerClient.DockerClient;
            int containers = 0, images = 0, volumes = 0, networks = 0;
            ulong reclaimed = 0;

            // Containers first: what they held (images, networks, volumes) only becomes unused afterwards.
            if (request.Containers)
            {
                var result = await docker.Containers.PruneContainersAsync(new ContainersPruneParameters { Filters = ContainerPruneFilters(request.KeepProjectContainers) }, cancellationToken);
                containers = result.ContainersDeleted?.Count ?? 0;
                reclaimed += result.SpaceReclaimed;
            }
            if (request.Networks)
            {
                var result = await docker.Networks.PruneNetworksAsync(new NetworksDeleteUnusedParameters(), cancellationToken);
                networks = result.NetworksDeleted?.Count ?? 0;
            }
            if (request.Images)
            {
                var result = await docker.Images.PruneImagesAsync(new ImagesPruneParameters { Filters = ImagePruneFilters(request.AllImages) }, cancellationToken);
                images = result.ImagesDeleted?.Count(i => !string.IsNullOrEmpty(i.Deleted)) ?? 0;
                reclaimed += result.SpaceReclaimed;
            }
            if (request.Volumes)
            {
                VolumesPruneResponse result;
                try
                {
                    // Since API 1.42 a plain prune only takes anonymous volumes; "all" includes named ones.
                    result = await docker.Volumes.PruneAsync(new VolumesPruneParameters { Filters = new Dictionary<string, IDictionary<string, bool>> { ["all"] = new Dictionary<string, bool> { ["true"] = true } } }, cancellationToken);
                }
                catch (Docker.DotNet.DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    result = await docker.Volumes.PruneAsync(new VolumesPruneParameters(), cancellationToken); // older daemon: no "all" filter
                }
                volumes = result.VolumesDeleted?.Count ?? 0;
                reclaimed += result.SpaceReclaimed;
            }
            return new PruneResultDto(containers, images, volumes, networks, (long)reclaimed);
        }

        /// <summary>The keep-rule: a stopped container that belongs to a compose project survives, so its
        /// project can start again and its images and volumes stay "in use" (and thus unpruned).</summary>
        public static IDictionary<string, IDictionary<string, bool>>? ContainerPruneFilters(bool keepProjectContainers)
            => keepProjectContainers
                ? new Dictionary<string, IDictionary<string, bool>> { ["label!"] = new Dictionary<string, bool> { ["com.docker.compose.project"] = true } }
                : null;

        /// <summary>dangling=true: only untagged images; dangling=false: every image no container uses.</summary>
        public static IDictionary<string, IDictionary<string, bool>> ImagePruneFilters(bool allUnused)
            => new Dictionary<string, IDictionary<string, bool>> { ["dangling"] = new Dictionary<string, bool> { [allUnused ? "false" : "true"] = true } };

    }
}
