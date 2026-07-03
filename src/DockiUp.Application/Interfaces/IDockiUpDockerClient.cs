using Docker.DotNet;

namespace DockiUp.Application.Interfaces
{
    public interface IDockiUpDockerClient
    {
        // Exposed as the Docker.DotNet interface (not the concrete client) so services that depend on it
        // can be unit-tested against a mocked daemon.
        IDockerClient DockerClient { get; }
    }
}
