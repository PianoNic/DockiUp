using DockiUp.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace DockiUp.API.Nodes
{
    /// <summary>Control-plane implementation of <see cref="INodeRpc"/>: routes a call to the target
    /// node's live connection and awaits its result.</summary>
    public class NodeRpc(IHubContext<NodeHub> hub, INodeRegistry registry) : INodeRpc
    {
        public Task<T> InvokeAsync<T>(Guid nodeId, string method, object?[] args, CancellationToken cancellationToken)
        {
            if (!registry.TryGetConnectionId(nodeId, out var connectionId))
                throw new NodeOfflineException(nodeId);
            return hub.Clients.Client(connectionId).InvokeCoreAsync<T>(method, args, cancellationToken);
        }
    }

}
