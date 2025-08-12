using DockiUp.Application.Dtos;
using DockiUp.Application.Models;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.RepresentationModel;

namespace DockiUp.Application.Queries
{
    public class GetEnvironmentFilesQuery : IRequest<List<ComposeDto>>
    {
        public GetEnvironmentFilesQuery(int containerDbId)
        {
            ContainerDbId = containerDbId;
        }

        public int ContainerDbId { get; set; }
    }

    public class GetEnvironmentFilesQueryHandler : IRequestHandler<GetEnvironmentFilesQuery, List<ComposeDto>>
    {

        public readonly DockiUpDbContext _DbContext;

        public GetEnvironmentFilesQueryHandler(DockiUpDbContext dbContext)
        {
            _DbContext = dbContext;
        }

        public async Task<List<ComposeDto>> Handle(GetEnvironmentFilesQuery request, CancellationToken cancellationToken)
        {
            var container = await _DbContext.Containers.SingleOrDefaultAsync(c => c.Id == request.ContainerDbId, cancellationToken)
                ?? throw new ArgumentException("Container not found.");

            var projectDirectory = container.Path;

            if (!Directory.Exists(projectDirectory))
                throw new DirectoryNotFoundException("Container directory not found.");

            var ymlFiles = Directory
                .EnumerateFiles(projectDirectory, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                            || file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var composeDtos = new List<ComposeDto>();

            foreach (var filePath in ymlFiles)
            {
                var input = File.ReadAllText(filePath);

                var yamlStream = new YamlStream();
                using (var reader = new StringReader(input))
                {
                    yamlStream.Load(reader);
                }

                var root = (YamlMappingNode)yamlStream.Documents[0].RootNode;

                if (!root.Children.ContainsKey("services"))
                    continue;

                var servicesNode = (YamlMappingNode)root.Children["services"];

                var services = new List<DockerService>();
                foreach (var service in servicesNode.Children)
                    services.Add(await ExtractDockerServices(service));


                composeDtos.Add(new ComposeDto
                {
                    FileName = Path.GetFileName(filePath),
                    Services = services
                });
            }

            return composeDtos;
        }

        private async Task<DockerService> ExtractDockerServices(KeyValuePair<YamlNode, YamlNode> service)
        {
            var serviceName = service.Key.ToString();
            var serviceNode = (YamlMappingNode)service.Value;

            var dockerService = new DockerService
            {
                ServiceName = serviceName,
                Ports = new List<string>(),
                EnvironmentVariables = new List<string>(),
                EnvFiles = new List<string>()
            };

            if (serviceNode.Children.ContainsKey("ports"))
            {
                var portsNode = (YamlSequenceNode)serviceNode.Children["ports"];
                foreach (var port in portsNode)
                    dockerService.Ports.Add(port.ToString());
            }

            if (serviceNode.Children.ContainsKey("environment"))
            {
                var envNode = (YamlSequenceNode)serviceNode.Children["environment"];
                foreach (var env in envNode)
                    dockerService.EnvironmentVariables.Add(env.ToString());
            }

            if (serviceNode.Children.ContainsKey("env_file"))
            {
                var envFileNode = serviceNode.Children["env_file"];
                if (envFileNode is YamlScalarNode scalarNode)
                    dockerService.EnvFiles.Add(scalarNode.Value ?? string.Empty);

                else if (envFileNode is YamlSequenceNode sequenceNode)
                {
                    foreach (var envFile in sequenceNode)
                        dockerService.EnvFiles.Add(envFile.ToString());
                }
            }

            return await Task.FromResult(dockerService);
        }
    }
}
