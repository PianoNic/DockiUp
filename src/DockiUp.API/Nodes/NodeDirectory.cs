using DockiUp.Application.Interfaces;

namespace DockiUp.API.Nodes
{
    /// <summary>Control-plane <see cref="INodeDirectory"/>: reports the nodes with a live connection.</summary>
    public class NodeDirectory(INodeRegistry registry) : INodeDirectory
    {
        public IReadOnlyList<Guid> GetOnlineNodeIds() => registry.OnlineLastSeen().Keys.ToList();
    }

}
