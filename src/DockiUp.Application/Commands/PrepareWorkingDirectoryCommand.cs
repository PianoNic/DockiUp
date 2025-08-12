using DockiUp.Application.Dtos;
using DockiUp.Application.Models;
using DockiUp.Domain.Enums;
using DockiUp.Domain.Models;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace DockiUp.Application.Commands
{
    public class PrepareWorkingDirectoryCommand : IRequest
    {
        public PrepareWorkingDirectoryCommand(WorkingDirectorySetupDto createContainerDto)
        {
            CreateContainerDto = createContainerDto;
        }

        public WorkingDirectorySetupDto CreateContainerDto { get; set; }
    }

    public class PrepareWorkingDirectoryCommandHandler : IRequestHandler<PrepareWorkingDirectoryCommand>
    {
        public readonly DockiUpDbContext _DbContext;
        private readonly SystemPaths _systemPaths;
        private readonly ILogger<PrepareWorkingDirectoryCommandHandler> _logger;

        public PrepareWorkingDirectoryCommandHandler(DockiUpDbContext dbContext, IOptions<SystemPaths> systemPaths, ILogger<PrepareWorkingDirectoryCommandHandler> logger)
        {
            _DbContext = dbContext;
            _systemPaths = systemPaths.Value;
            _logger = logger;
        }

        public async Task Handle(PrepareWorkingDirectoryCommand request, CancellationToken cancellationToken)
        {
            string projectsPath = _systemPaths.ProjectsPath;
            string repoName = request.CreateContainerDto.Name;
            string clonePath = Path.Combine(projectsPath, repoName);

            _logger.LogInformation($"Cloning repository to {projectsPath}");

            if (Directory.Exists(clonePath))
                throw new ArgumentException("Container already exists.");

            // Ensure the projects directory exists
            Directory.CreateDirectory(projectsPath);

            // Run git clone command
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone {request.CreateContainerDto.GitUrl} \"{clonePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    _logger.LogInformation($"Repository cloned successfully to {clonePath}");

                    var container = new Container
                    {
                        Name = request.CreateContainerDto.Name,
                        Description = request.CreateContainerDto.Description ?? "No description provided",
                        GitUrl = request.CreateContainerDto.GitUrl,
                        Path = clonePath,
                        LastUpdated = DateTime.UtcNow,
                        Status = StatusType.Stopped,
                        SetupStatus = ContainerSetupStatus.Created,
                    };

                    await _DbContext.Containers.AddAsync(container, cancellationToken);
                    await _DbContext.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation($"Container '{container.Name}' created successfully with ID: {container.Id}");
                }
                else
                    throw new ArgumentException($"Failed to clone repository. Error: {error}");
            }
        }
    }
}