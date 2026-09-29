using System.Diagnostics;
using DockiUp.Application.Interfaces;
using DockiUp.Infrastructure;
using DockiUp.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Nodes
{
    [ApiController]
    [Route("api/[controller]")]
    public class NodesController(
        DockiUpDbContext db,
        INodeRegistry registry,
        IHubContext<NodeHub> hub,
        IConfiguration configuration,
        IActivityLogger activity) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<NodeDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            var online = registry.OnlineLastSeen();
            var nodes = await db.Nodes.OrderBy(n => n.Name).ToListAsync(cancellationToken);
            var result = nodes.Select(n => new NodeDto(
                n.Id,
                n.Name,
                n.MachineName,
                n.Os,
                n.DockerVersion,
                Online: online.ContainsKey(n.Id),
                // "Pending" until the node first connects and reports its machine details.
                Pending: !online.ContainsKey(n.Id) && string.IsNullOrEmpty(n.MachineName),
                FirstSeenAt: n.CreatedAt,
                LastSeenAt: online.TryGetValue(n.Id, out var seen) ? seen.UtcDateTime : n.LastSeenAt));
            return Ok(result);
        }

        /// <summary>Generates a fresh node token + the URL the node should dial, WITHOUT persisting
        /// anything. The UI builds the copy-paste node compose from this and only saves on demand.</summary>
        [HttpGet("draft")]
        [ProducesResponseType(typeof(NodeDraftDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult Draft()
        {
            // A node dials this URL to reach the control plane, so without it there's no compose worth
            // showing. It must be reachable from the node's host - never localhost.
            var serverUrl = (configuration["DockiUp:PublicUrl"]
                ?? Environment.GetEnvironmentVariable("PUBLIC_URL") ?? "").TrimEnd('/');
            if (string.IsNullOrWhiteSpace(serverUrl))
                return BadRequest(new ProblemDetails
                {
                    Title = "Public URL not configured",
                    Detail = "Set PUBLIC_URL on the control plane to the URL nodes should dial (e.g. https://dockiup.example.com) - it must be reachable from the node's host, not localhost. Then reopen this dialog.",
                    Status = StatusCodes.Status400BadRequest,
                });

            return Ok(new NodeDraftDto(GenerateNodeName(), NodeTokenHasher.Generate(), serverUrl));
        }

        // A simple docker-style adjective-animal suggestion (e.g. "brave-otter") the user can rename
        // in the modal before saving.
        private static readonly string[] Adjectives =
            ["brave", "calm", "clever", "eager", "gentle", "happy", "jolly", "keen", "lively", "merry", "proud", "swift"];
        private static readonly string[] Animals =
            ["otter", "panda", "falcon", "lynx", "heron", "badger", "ferret", "marten", "gecko", "raven", "tapir", "yak"];

        private static string GenerateNodeName()
        {
            var adj = Adjectives[Random.Shared.Next(Adjectives.Length)];
            var animal = Animals[Random.Shared.Next(Animals.Length)];
            return $"{adj}-{animal}";
        }

        /// <summary>Persists a node from the Add-node modal (stores only the token hash). The node
        /// shows as pending until it dials in with this token.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(NodeDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CreateNodeRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return BadRequest("A token is required.");

            var name = string.IsNullOrWhiteSpace(request.Name) ? "node" : request.Name.Trim();
            var node = new Domain.Node { Name = name, TokenHash = NodeTokenHasher.Hash(request.Token) };
            db.Nodes.Add(node);
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("node.create", node.Name, cancellationToken: cancellationToken);

            var dto = new NodeDto(node.Id, node.Name, node.MachineName, node.Os, node.DockerVersion,
                Online: false, Pending: true, node.CreatedAt, node.LastSeenAt);
            return CreatedAtAction(nameof(List), dto);
        }

        /// <summary>Removes a node and revokes its token.</summary>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            var node = await db.Nodes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
            if (node is null) return NotFound();

            db.Nodes.Remove(node);
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("node.delete", node.Name, cancellationToken: cancellationToken);
            return NoContent();
        }

        /// <summary>Round-trips a ping to the node to prove the channel is live. Returns the node's
        /// reply and the measured round-trip time. 404 if the node is offline.</summary>
        [HttpPost("{id:guid}/ping")]
        [ProducesResponseType(typeof(NodePingResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Ping(Guid id, CancellationToken cancellationToken)
        {
            if (!registry.TryGetConnectionId(id, out var connectionId))
                return NotFound();

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reply = await hub.Clients.Client(connectionId).InvokeAsync<string>("Ping", cancellationToken);
                stopwatch.Stop();
                return Ok(new NodePingResultDto(reply, (int)stopwatch.ElapsedMilliseconds));
            }
            catch (IOException)
            {
                // Connection dropped between the lookup and the invoke.
                return NotFound();
            }
        }
    }
}
