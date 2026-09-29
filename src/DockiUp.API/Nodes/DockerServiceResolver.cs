using DockiUp.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace DockiUp.API.Nodes
{
    /// <summary>Returns the local <see cref="IDockerService"/> for null (control-plane) projects, or
    /// a <see cref="RemoteDockerService"/> bound to the target node otherwise.</summary>
    public class DockerServiceResolver(IDockerService local, IHubContext<NodeHub> hub, INodeRegistry registry, DeployLogRelay logRelay)
        : IDockerServiceResolver
    {
        public IDockerService Resolve(Guid? nodeId)
            => nodeId is null ? local : new RemoteDockerService(nodeId.Value, hub, registry, logRelay);
    }
}
