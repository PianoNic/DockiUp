using System.Diagnostics.CodeAnalysis;
using Docker.DotNet;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using Microsoft.Extensions.Options;

namespace DockiUp.Infrastructure.Clients
{
    /// <summary>
    /// Docker client configured from SystemPaths (Komodo-style: optional socket path for single host).
    /// Thin factory over Docker.DotNet - only meaningfully exercised against a real daemon (integration).
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class DockiUpDockerClient : IDockiUpDockerClient
    {
        public IDockerClient DockerClient { get; }

        public DockiUpDockerClient(IOptions<SystemPaths> systemPaths)
        {
            var path = systemPaths.Value.DockerSocket;
            var config = !string.IsNullOrWhiteSpace(path)
                ? new DockerClientConfiguration(new Uri(path))
                : new DockerClientConfiguration();
            DockerClient = config.CreateClient();
        }
    }
}
