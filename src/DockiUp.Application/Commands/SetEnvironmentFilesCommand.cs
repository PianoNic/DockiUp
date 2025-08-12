using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace DockiUp.Application.Commands
{
    public class SetEnvironmentFilesCommand : IRequest
    {
        public SetEnvironmentFilesCommand(int containerDbId, ComposeDto composeDto)
        {
            ContainerDbId = containerDbId;
            ComposeDto = composeDto;
        }

        public int ContainerDbId { get; set; }
        public ComposeDto ComposeDto { get; set; }
    }
}

public class SetEnvironmentFilesCommandHandler : IRequestHandler<SetEnvironmentFilesCommand>
{
    private readonly DockiUpDbContext _dbContext;

    public SetEnvironmentFilesCommandHandler(DockiUpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Handle(SetEnvironmentFilesCommand request, CancellationToken cancellationToken)
    {
        var container = await _dbContext.Containers
            .SingleOrDefaultAsync(c => c.Id == request.ContainerDbId, cancellationToken)
            ?? throw new ArgumentException("Container not found.");

        var ymlFile = Directory
            .EnumerateFiles(container.Path, "*.*", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(request.ComposeDto.FileName, StringComparison.OrdinalIgnoreCase));

        if (ymlFile == null)
            throw new FileNotFoundException($"Compose file '{request.ComposeDto.FileName}' not found in container path.", request.ComposeDto.FileName);

        var originalContent = await File.ReadAllTextAsync(ymlFile, cancellationToken);

        var yaml = new YamlStream();
        using (var reader = new StringReader(originalContent))
        {
            yaml.Load(reader);
        }

        var root = (YamlMappingNode)yaml.Documents[0].RootNode;

        if (!root.Children.TryGetValue(new YamlScalarNode("services"), out var servicesNode))
        {
            servicesNode = new YamlMappingNode();
            root.Add(new YamlScalarNode("services"), servicesNode);
        }

        var serviceMapping = (YamlMappingNode)servicesNode;

        bool contentModified = false;

        foreach (var serviceDto in request.ComposeDto.Services)
        {
            if (string.IsNullOrEmpty(serviceDto.ServiceName))
                continue;

            var serviceKey = new YamlScalarNode(serviceDto.ServiceName);

            // Get or create the service node
            if (!serviceMapping.Children.TryGetValue(serviceKey, out var serviceNodeRaw))
            {
                serviceNodeRaw = new YamlMappingNode();
                serviceMapping.Add(serviceKey, serviceNodeRaw);
                contentModified = true;
            }

            var serviceNode = (YamlMappingNode)serviceNodeRaw;

            // --- Update environment variables ---
            if (serviceDto.EnvironmentVariables?.Any() == true)
            {
                // Get existing environment vars if they exist
                var existingEnvVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (serviceNode.Children.TryGetValue(new YamlScalarNode("environment"), out var existingEnvNode))
                {
                    if (existingEnvNode is YamlSequenceNode envSeq)
                    {
                        foreach (var entry in envSeq)
                        {
                            var entryText = entry.ToString();
                            var parts = entryText.Split('=', 2);
                            if (parts.Length == 2)
                                existingEnvVars[parts[0]] = parts[1];
                        }
                    }
                }

                // Check if there are any changes to make
                bool envVarsChanged = false;
                foreach (var newEnv in serviceDto.EnvironmentVariables)
                {
                    var parts = newEnv.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        if (!existingEnvVars.TryGetValue(parts[0], out var existingValue) || existingValue != parts[1])
                        {
                            existingEnvVars[parts[0]] = parts[1];
                            envVarsChanged = true;
                        }
                    }
                }

                if (envVarsChanged)
                {
                    var newEnvSequence = new YamlSequenceNode(existingEnvVars.Select(kvp => new YamlScalarNode($"{kvp.Key}={kvp.Value}")));
                    serviceNode.Children.Remove(new YamlScalarNode("environment"));
                    serviceNode.Add(new YamlScalarNode("environment"), newEnvSequence);
                    contentModified = true;
                }
            }

            // --- Update env_file ---
            if (serviceDto.EnvFiles?.Any() == true)
            {
                bool needsUpdate = true;

                // Check if existing env_file is identical
                if (serviceNode.Children.TryGetValue(new YamlScalarNode("env_file"), out var existingEnvFile))
                {
                    if (serviceDto.EnvFiles.Count == 1 && existingEnvFile is YamlScalarNode scalarNode &&
                        scalarNode.Value == serviceDto.EnvFiles.First())
                    {
                        needsUpdate = false;
                    }
                    else if (existingEnvFile is YamlSequenceNode seqNode)
                    {
                        var existingFiles = seqNode.Children.Select(n => n.ToString()).ToList();
                        if (existingFiles.Count == serviceDto.EnvFiles.Count &&
                            !existingFiles.Except(serviceDto.EnvFiles).Any() &&
                            !serviceDto.EnvFiles.Except(existingFiles).Any())
                        {
                            needsUpdate = false;
                        }
                    }
                }

                if (needsUpdate)
                {
                    YamlNode envFileNode = serviceDto.EnvFiles.Count == 1
                        ? new YamlScalarNode(serviceDto.EnvFiles.First())
                        : new YamlSequenceNode(serviceDto.EnvFiles.Select(e => new YamlScalarNode(e)));

                    serviceNode.Children.Remove(new YamlScalarNode("env_file"));
                    serviceNode.Add(new YamlScalarNode("env_file"), envFileNode);
                    contentModified = true;
                }
            }

            // --- Update ports ---
            if (serviceDto.Ports?.Any() == true)
            {
                bool needsUpdate = true;

                // Check if existing ports are identical
                if (serviceNode.Children.TryGetValue(new YamlScalarNode("ports"), out var existingPorts) &&
                    existingPorts is YamlSequenceNode portsSeq)
                {
                    var existingPortsList = portsSeq.Children.Select(n => n.ToString()).ToList();
                    if (existingPortsList.Count == serviceDto.Ports.Count &&
                        !existingPortsList.Except(serviceDto.Ports).Any() &&
                        !serviceDto.Ports.Except(existingPortsList).Any())
                    {
                        needsUpdate = false;
                    }
                }

                if (needsUpdate)
                {
                    var portsNode = new YamlSequenceNode(serviceDto.Ports.Select(p => new YamlScalarNode(p)));
                    serviceNode.Children.Remove(new YamlScalarNode("ports"));
                    serviceNode.Add(new YamlScalarNode("ports"), portsNode);
                    contentModified = true;
                }
            }
        }

        // Only save the file if we actually made changes
        if (contentModified)
        {
            // Use a StringBuilder to avoid the "..." at the end
            var sb = new StringBuilder();
            using (var writer = new StringWriter(sb))
            {
                yaml.Save(writer, assignAnchors: false);
            }

            // Remove the document end marker if present
            string output = sb.ToString().TrimEnd();
            if (output.EndsWith("..."))
            {
                output = output.Substring(0, output.Length - 3).TrimEnd();
            }

            await File.WriteAllTextAsync(ymlFile, output, cancellationToken);
        }
    }
}