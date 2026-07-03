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
        private const string ComposeFileName = "dockiup_compose.yml";
        public DockerService(IDockiUpDockerClient dockiUpDockerClient, IDockiUpDbContext dbContext, IOptions<SystemPaths> systemPaths)
        {
            _dockiUpDockerClient = dockiUpDockerClient;
            _dbContext = dbContext;
            _systemPaths = systemPaths.Value;
        }

        public async Task<ProjectDto[]> GetRawProjectsAsync()
        {
            var containers = await _dockiUpDockerClient.DockerClient.Containers
                .ListContainersAsync(new ContainersListParameters { All = true });

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
                    UpdateMethod = null
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
                    project.ProjectDescription = proj.Description ?? "Not Managed By DockiUp";
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
        public Task StartProjectAsync(string folderPath)
            => RunComposeAsync("up -d", folderPath, "Start successfully", throwOnFailure: true);

        // Runs a `docker compose` subcommand in the project folder. The compose CLI - unlike Docker.DotNet -
        // takes no socket from our config, so when SystemPaths.DockerSocket is set we point DOCKER_HOST at
        // the same daemon. Otherwise compose would silently target the default socket while container
        // operations (via Docker.DotNet) use the configured one - a split brain on non-default setups.
        [ExcludeFromCodeCoverage] // spawns the `docker` CLI process; covered by integration/E2E
        private async Task RunComposeAsync(string composeArgs, string folderPath, string successMessage, bool throwOnFailure)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"compose -f {ComposeFileName} {composeArgs}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = folderPath
                }
            };
            if (!string.IsNullOrWhiteSpace(_systemPaths.DockerSocket))
                process.StartInfo.Environment["DOCKER_HOST"] = _systemPaths.DockerSocket;

            process.Start();

            // Drain both pipes before waiting so a chatty compose can't deadlock on a full buffer.
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                Console.WriteLine(successMessage);
            }
            else if (throwOnFailure)
            {
                throw new ArgumentException("Docker Compose failed", error);
            }
            else
            {
                Console.WriteLine("Docker Compose failed");
                Console.WriteLine(error);
            }
        }

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

        [ExcludeFromCodeCoverage] // shells out to the `docker compose` CLI; covered by integration/E2E
        public Task StopProjectAsync(string folderPath)
            => RunComposeAsync("down", folderPath, "Stop successfully", throwOnFailure: false);

        [ExcludeFromCodeCoverage] // shells out to the `docker compose` CLI; covered by integration/E2E
        public Task RestartProjectAsync(string folderPath)
            => RunComposeAsync("restart", folderPath, "Restart successfully", throwOnFailure: false);

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
    }
}
