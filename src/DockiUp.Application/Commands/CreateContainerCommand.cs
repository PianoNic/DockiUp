using DockiUp.Application.Dtos;
using DockiUp.Application.Models;
using DockiUp.Domain.Enums;
using DockiUp.Domain.Models;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace DockiUp.Application.Commands
{
    public class CreateContainerCommand : IRequest<ComposeInfoDto>
    {
        public CreateContainerCommand(CreateContainerDto createContainerDto)
        {
            CreateContainerDto = createContainerDto;
        }

        public CreateContainerDto CreateContainerDto { get; set; }
    }

    public class CreateContainerCommandHandler : IRequestHandler<CreateContainerCommand, ComposeInfoDto>
    {
        public readonly DockiUpDbContext _DbContext;
        private readonly SystemPaths _systemPaths;
        private readonly ILogger<CreateContainerCommandHandler> _logger;

        public CreateContainerCommandHandler(DockiUpDbContext dbContext, IOptions<SystemPaths> systemPaths, ILogger<CreateContainerCommandHandler> logger)
        {
            _DbContext = dbContext;
            _systemPaths = systemPaths.Value;
            _logger = logger;
        }

        public async Task<ComposeInfoDto> Handle(CreateContainerCommand request, CancellationToken cancellationToken)
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

                    // Parse the docker-compose.yml file
                    var composeFilePath = Path.Combine(clonePath, "docker-compose.yml");

                    if (!File.Exists(composeFilePath))
                        throw new ArgumentException("docker-compose.yml not found in the repository.");

                    var composeInfoDto = await ParseComposeFile(composeFilePath);

                    var container = new Container
                    {
                        Name = request.CreateContainerDto.Name,
                        Description = request.CreateContainerDto.Description ?? "No description provided",
                        GitUrl = request.CreateContainerDto.GitUrl,
                        Path = clonePath,
                        LastUpdated = DateTime.UtcNow,
                        LastGitPush = DateTime.UtcNow,
                        Status = StatusType.Stopped,
                        UpdateMethod = request.CreateContainerDto.UpdateMethod,
                    };

                    await _DbContext.Containers.AddAsync(container, cancellationToken);
                    await _DbContext.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation($"Container '{container.Name}' created successfully with ID: {container.Id}");

                    return composeInfoDto;  // Return the DTO
                }
                else
                    throw new ArgumentException($"Failed to clone repository. Error: {error}");
            }
        }

        private async Task<ComposeInfoDto> ParseComposeFile(string composeFilePath)
        {
            var composeInfoDto = new ComposeInfoDto { Services = new List<ComposeDto>() };

            var input = File.ReadAllText(composeFilePath);

            var yamlStream = new YamlStream();
            using (var reader = new StringReader(input))
            {
                yamlStream.Load(reader);
            }

            var root = (YamlMappingNode)yamlStream.Documents[0].RootNode;

            // Iterate over services defined in the compose file
            if (root.Children.ContainsKey("services"))
            {
                var servicesNode = (YamlMappingNode)root.Children["services"];
                foreach (var service in servicesNode.Children)
                {
                    var serviceName = service.Key.ToString();
                    var serviceNode = (YamlMappingNode)service.Value;

                    var serviceDto = new ComposeDto
                    {
                        ServiceName = serviceName,
                        Ports = new List<string>(),
                        EnvironmentVariables = new List<string>()
                    };

                    if (serviceNode.Children.ContainsKey("ports"))
                    {
                        var portsNode = (YamlSequenceNode)serviceNode.Children["ports"];
                        foreach (var port in portsNode)
                        {
                            serviceDto.Ports.Add(port.ToString());
                        }
                    }

                    if (serviceNode.Children.ContainsKey("environment"))
                    {
                        var envNode = (YamlSequenceNode)serviceNode.Children["environment"];
                        foreach (var env in envNode)
                        {
                            serviceDto.EnvironmentVariables.Add(env.ToString());
                        }
                    }

                    composeInfoDto.Services.Add(serviceDto);
                }
            }

            return composeInfoDto;
        }
    }
}