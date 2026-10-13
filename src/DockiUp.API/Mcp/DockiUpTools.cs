using System.ComponentModel;
using DockiUp.Application.Commands;
using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Application.ImageUpdates;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Queries;
using DockiUp.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Toamaisutaa.Abstractions;

namespace DockiUp.API.Mcp
{
    /// <summary>What AI clients can do over MCP (/mcp). Same sign-in as the web UI. Nothing here deletes, prunes or
    /// reads variables/secrets; those stay in the UI.</summary>
    [McpServerToolType]
    public sealed class DockiUpTools(IMediator mediator, IDockiUpDbContext db, ICurrentUser? currentUser = null)
    {
        private const string HostHint = "Host name as list_projects shows it; only needed when the project runs on several hosts.";

        [McpServerTool(Name = "list_projects", ReadOnly = true), Description("Lists every compose project on every host with its containers and their state.")]
        public async Task<object[]> ListProjects(CancellationToken ct)
        {
            var hosts = await HostNames(ct);
            return (await mediator.Send(new GetProjectsQuery(), ct)).Select(p => (object)new
            {
                project = p.DockerProjectName,
                host = hosts(p.NodeId),
                managedByDockiUp = p.ManagedByDockiUp,
                deployedCommit = p.DeployedCommit,
                deployedAt = p.DeployedAt,
                containers = p.Containers.Select(c => new { c.Name, service = c.ServiceName, state = c.State.ToString(), c.Status }),
            }).ToArray();
        }

        [McpServerTool(Name = "get_logs", ReadOnly = true), Description("Recent log lines of a project's containers, with UTC timestamps.")]
        public async Task<string> GetLogs(
            [Description("Compose project name")] string project,
            [Description(HostHint)] string? host = null,
            [Description("Only this compose service")] string? service = null,
            [Description("Lines per container")] int tail = 200,
            CancellationToken ct = default)
        {
            var p = await FindProject(project, host, ct);
            var containers = p.Containers.Where(c => service is null || c.ServiceName.Equals(service, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (containers.Length == 0) throw new McpException($"{p.DockerProjectName} has no container for service '{service}'.");

            var parts = new List<string>();
            foreach (var c in containers)
                parts.Add($"== {c.Name} ==\n{await mediator.Send(new GetContainerLogsQuery(c.Id, Math.Clamp(tail, 1, 2000), p.NodeId, timestamps: true), ct)}");
            return string.Join("\n", parts);
        }

        [McpServerTool(Name = "list_deployments", ReadOnly = true), Description("Recent deployments of a project DockiUp manages, newest first.")]
        public async Task<DeploymentDto[]> ListDeployments(
            [Description("Compose project name")] string project,
            [Description(HostHint)] string? host = null,
            int limit = 10,
            CancellationToken ct = default)
            => await mediator.Send(new ListDeploymentsQuery(ManagedId(await FindProject(project, host, ct)), Math.Clamp(limit, 1, 50)), ct);

        [McpServerTool(Name = "get_deployment", ReadOnly = true), Description("One deployment with its full log (secrets are redacted).")]
        public async Task<DeploymentDto> GetDeployment([Description("Deployment id from list_deployments")] Guid deploymentId, CancellationToken ct)
            => await mediator.Send(new GetDeploymentQuery(deploymentId), ct);

        [McpServerTool(Name = "list_image_updates", ReadOnly = true), Description("Services whose image has a newer version for the tag they run.")]
        public async Task<object[]> ListImageUpdates(CancellationToken ct)
        {
            var names = await db.ProjectInfo.ToDictionaryAsync(p => p.Id, p => p.DockerProjectName, ct);
            return (await mediator.Send(new ListImageUpdatesQuery(), ct))
                .Where(u => u.UpdateAvailable)
                .Select(u => (object)new { project = names.GetValueOrDefault(u.ProjectId), service = u.ServiceName, u.Image, u.CheckedAt })
                .ToArray();
        }

        [McpServerTool(Name = "deploy", Destructive = false), Description("Queues a deployment of a project DockiUp manages (pull, build, compose up), like Deploy now in the UI.")]
        public async Task<DeploymentDto> Deploy(
            [Description("Compose project name")] string project,
            [Description(HostHint)] string? host = null,
            CancellationToken ct = default)
            => await mediator.Send(new QueueDeploymentCommand(ManagedId(await FindProject(project, host, ct)), DeploymentTrigger.Manual, Actor), ct);

        [McpServerTool(Name = "control_project", Destructive = false), Description("Starts, stops or restarts all containers of a project.")]
        public async Task<string> ControlProject(
            [Description("Compose project name")] string project,
            ProjectAction action,
            [Description(HostHint)] string? host = null,
            CancellationToken ct = default)
        {
            var p = await FindProject(project, host, ct);
            await mediator.Send(new ProjectLifecycleCommand(action, p.Id, p.DockerProjectName, p.NodeId), ct);
            return $"{action} sent to {p.DockerProjectName}.";
        }

        private string? Actor => currentUser?.IsAuthenticated == true ? currentUser.Name : null;

        private async Task<Func<Guid?, string>> HostNames(CancellationToken ct)
        {
            var nodes = await db.Nodes.ToDictionaryAsync(n => n.Id, n => n.Name, ct);
            return id => id is { } n ? nodes.GetValueOrDefault(n, "node") : "local";
        }

        // Names repeat across hosts (that's why the UI's URLs carry a host code), so an ambiguous name needs the host.
        private async Task<ProjectDto> FindProject(string project, string? host, CancellationToken ct)
        {
            var hosts = await HostNames(ct);
            var matches = (await mediator.Send(new GetProjectsQuery(), ct))
                .Where(p => p.DockerProjectName.Equals(project, StringComparison.OrdinalIgnoreCase))
                .Where(p => host is null || hosts(p.NodeId).Equals(host, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new McpException($"No project '{project}'{(host is null ? "" : $" on {host}")}. list_projects shows them all."),
                _ => throw new McpException($"'{project}' runs on {string.Join(", ", matches.Select(p => hosts(p.NodeId)))}; pass host."),
            };
        }

        private static Guid ManagedId(ProjectDto p)
            => p.ManagedByDockiUp && p.Id is { } id ? id : throw new McpException($"{p.DockerProjectName} isn't managed by DockiUp, so it has no deployments.");
    }
}
