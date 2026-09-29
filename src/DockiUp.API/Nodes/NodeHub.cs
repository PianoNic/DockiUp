using DockiUp.API.SignalR;
using DockiUp.Domain;
using DockiUp.Infrastructure;
using DockiUp.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Nodes
{
    /// <summary>Control-plane endpoint that nodes dial into. Nodes authenticate with a pre-shared
    /// token (not the app's user auth), so the hub is AllowAnonymous and validates the token itself
    /// in OnConnectedAsync against the persisted Node rows. The control plane invokes Docker/compose
    /// operations back on the node connection; the node invokes <c>Register</c>/<c>Heartbeat</c> here.</summary>
    [AllowAnonymous]
    public class NodeHub(
        INodeRegistry registry,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        IHubContext<DockiUpHub> containerHub,
        IExecRelay execRelay,
        DeployLogRelay deployLogRelay,
        ILogger<NodeHub> logger) : Hub
    {
        // Key under which a token-resolved node Id is stashed on the connection, so Register can use
        // it instead of trusting whatever Id the node self-reports.
        private const string ResolvedNodeIdKey = "ResolvedNodeId";

        public override async Task OnConnectedAsync()
        {
            var token = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            if (string.IsNullOrEmpty(token))
            {
                logger.LogWarning("Rejected node connection {ConnectionId}: missing token.", Context.ConnectionId);
                Context.Abort();
                return;
            }

            // A token persisted on a Node row (from the Add-node UI) authorizes the connection AND
            // identifies the node - so the node doesn't need to know its own Id.
            var hash = NodeTokenHasher.Hash(token);
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DockiUpDbContext>();
                var match = await db.Nodes.FirstOrDefaultAsync(n => n.TokenHash == hash);
                if (match is not null)
                {
                    Context.Items[ResolvedNodeIdKey] = match.Id;
                    await base.OnConnectedAsync();
                    return;
                }
            }

            // Fall back to a static allow-list (Node:Tokens); those nodes self-report their Id.
            var allowed = configuration.GetSection("Node:Tokens").Get<string[]>() ?? [];
            if (!allowed.Contains(token, StringComparer.Ordinal))
            {
                logger.LogWarning("Rejected node connection {ConnectionId}: unknown token.", Context.ConnectionId);
                Context.Abort();
                return;
            }

            await base.OnConnectedAsync();
        }

        /// <summary>Called by the node right after connecting (and after every reconnect) to report
        /// who it is. Persists/refreshes the Node row and records the live connection.</summary>
        public async Task Register(NodeRegistrationDto registration)
        {
            // A token-resolved Id (pre-created node) is authoritative; otherwise trust the self-reported Id.
            Guid nodeId;
            if (Context.Items.TryGetValue(ResolvedNodeIdKey, out var resolved) && resolved is Guid resolvedId)
            {
                nodeId = resolvedId;
            }
            else if (!Guid.TryParse(registration.Id, out nodeId))
            {
                logger.LogWarning("Node on {ConnectionId} sent an invalid Id '{Id}'; aborting.", Context.ConnectionId, registration.Id);
                Context.Abort();
                return;
            }

            registry.Register(nodeId, Context.ConnectionId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DockiUpDbContext>();
            var node = await db.Nodes.FirstOrDefaultAsync(n => n.Id == nodeId);
            if (node is null)
            {
                db.Nodes.Add(new Node
                {
                    Id = nodeId,
                    Name = registration.Name,
                    MachineName = registration.MachineName,
                    Os = registration.Os,
                    DockerVersion = registration.DockerVersion,
                    LastSeenAt = DateTime.UtcNow,
                });
            }
            else
            {
                // The name set at creation (UI) is authoritative; only adopt the reported one for rows
                // that never had a name. Runtime details always refresh.
                if (string.IsNullOrWhiteSpace(node.Name)) node.Name = registration.Name;
                node.MachineName = registration.MachineName;
                node.Os = registration.Os;
                node.DockerVersion = registration.DockerVersion;
                node.LastSeenAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();

            logger.LogInformation("Node {Name} ({NodeId}) registered on {ConnectionId}.", node?.Name ?? registration.Name, nodeId, Context.ConnectionId);
        }

        /// <summary>Periodic liveness ping from the node.</summary>
        public Task Heartbeat()
        {
            registry.Touch(Context.ConnectionId);
            return Task.CompletedTask;
        }

        /// <summary>Console output from a node-run exec session - forward to the browser that opened it.</summary>
        public Task ExecOutput(string sessionId, string base64Data)
        {
            if (execRelay.TryGetExec(sessionId, out _, out var browserConnectionId))
                return containerHub.Clients.Client(browserConnectionId).SendAsync("ExecOutput", sessionId, base64Data);
            return Task.CompletedTask;
        }

        /// <summary>A node-run exec session exited - forward the code to the browser and forget it.</summary>
        public Task ExecExited(string sessionId, long? exitCode)
        {
            if (execRelay.TryGetExec(sessionId, out _, out var browserConnectionId))
                _ = containerHub.Clients.Client(browserConnectionId).SendAsync("ExecExited", sessionId, exitCode);
            execRelay.RemoveExec(sessionId);
            return Task.CompletedTask;
        }

        /// <summary>A line of deploy-pipeline output from a node - hand it to the server call awaiting it.</summary>
        public Task DeployLog(string runId, string line) => deployLogRelay.WriteAsync(runId, line);

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            registry.Remove(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }
    }
}
