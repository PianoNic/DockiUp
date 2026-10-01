using DockiUp.Application.Commands;
using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Queries;
using DockiUp.Domain;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Toamaisutaa.Abstractions;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectController(IMediator mediator, IDockiUpDbContext db, IConfiguration configuration, ICurrentUser? currentUser = null)
        : ControllerBase
    {
        // Only set when OIDC is on; queued work is attributed to whoever asked for it.
        private string? Actor => currentUser?.IsAuthenticated == true ? currentUser.Name : null;

        /// <summary>Creates a project and queues its first deployment (returned).</summary>
        [HttpPost("DeployProject", Name = "DeployProject")]
        [ProducesResponseType(typeof(DeploymentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<DeploymentDto?>> DeployProject([FromBody] SetupProjectDto setupDto)
            => Ok(await mediator.Send(new DeployProjectCommand(setupDto), HttpContext.RequestAborted));

        [HttpGet("GetProjects", Name = "GetProjects")]
        [ProducesResponseType(typeof(ProjectDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<ProjectDto[]>> GetContainers()
            => Ok(await mediator.Send(new GetProjectsQuery(), HttpContext.RequestAborted));

        [HttpGet("GetProject", Name = "GetProject")]
        [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProjectDto>> GetProject([FromQuery] Guid? projectId, [FromQuery] string? dockerProjectName)
        {
            var project = await mediator.Send(new GetProjectQuery(projectId, dockerProjectName), HttpContext.RequestAborted);
            return project is null ? NotFound() : Ok(project);
        }

        // Lifecycle works for every compose project: by DockiUp id, or by docker name (+ node) for the rest.
        [HttpPost("StartProject", Name = "StartProject")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public Task<ActionResult> StartProject([FromQuery] Guid? projectId, [FromQuery] string? dockerProjectName, [FromQuery] Guid? nodeId)
            => Lifecycle(ProjectAction.Start, projectId, dockerProjectName, nodeId);

        [HttpPost("StopProject", Name = "StopProject")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public Task<ActionResult> StopProject([FromQuery] Guid? projectId, [FromQuery] string? dockerProjectName, [FromQuery] Guid? nodeId)
            => Lifecycle(ProjectAction.Stop, projectId, dockerProjectName, nodeId);

        [HttpPost("RestartProject", Name = "RestartProject")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public Task<ActionResult> RestartProject([FromQuery] Guid? projectId, [FromQuery] string? dockerProjectName, [FromQuery] Guid? nodeId)
            => Lifecycle(ProjectAction.Restart, projectId, dockerProjectName, nodeId);

        /// <summary>`compose down` for any project; a DockiUp project is also forgotten (record, history, folder).</summary>
        [HttpPost("RemoveProject", Name = "RemoveProject")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> RemoveProject([FromQuery] Guid? projectId, [FromQuery] string? dockerProjectName, [FromQuery] Guid? nodeId, [FromQuery] bool removeVolumes = false)
        {
            await mediator.Send(new RemoveProjectCommand(projectId, dockerProjectName, nodeId, removeVolumes), HttpContext.RequestAborted);
            return NoContent();
        }

        private async Task<ActionResult> Lifecycle(ProjectAction action, Guid? projectId, string? dockerProjectName, Guid? nodeId)
        {
            await mediator.Send(new ProjectLifecycleCommand(action, projectId, dockerProjectName, nodeId), HttpContext.RequestAborted);
            return NoContent();
        }

        /// <summary>Downloads newer images without restarting anything; the next deploy applies them.</summary>
        [HttpPost("{projectId:guid}/Pull", Name = "PullProjectImages")]
        [ProducesResponseType(typeof(PullResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PullResultDto>> PullProjectImages(Guid projectId)
            => Ok(new PullResultDto(await mediator.Send(new PullProjectImagesCommand(projectId), HttpContext.RequestAborted)));

        /// <summary>Queues a deployment now (git sync + compose pull/up). Returns the queued record.</summary>
        [HttpPost("{projectId:guid}/Deploy", Name = "QueueDeployment")]
        [ProducesResponseType(typeof(DeploymentDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeploymentDto>> QueueDeployment(Guid projectId)
            => Accepted(await mediator.Send(new QueueDeploymentCommand(projectId, DeploymentTrigger.Manual, Actor), HttpContext.RequestAborted));

        /// <summary>Redeploys the exact commit an earlier deployment shipped (rollback or roll forward).</summary>
        [HttpPost("Deployments/{deploymentId:guid}/Redeploy", Name = "RedeployVersion")]
        [ProducesResponseType(typeof(DeploymentDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeploymentDto>> RedeployVersion(Guid deploymentId)
        {
            var source = await mediator.Send(new GetDeploymentQuery(deploymentId), HttpContext.RequestAborted);
            if (source.CommitAfter is null)
                return BadRequest(new ProblemDetails { Title = "Nothing to redeploy", Detail = "That deployment has no recorded commit (not a git project, or it failed before syncing).", Status = 400 });
            return Accepted(await mediator.Send(
                new QueueDeploymentCommand(source.ProjectId, DeploymentTrigger.Rollback, Actor, source.CommitAfter), HttpContext.RequestAborted));
        }

        [HttpGet("{projectId:guid}/Deployments", Name = "ListDeployments")]
        [ProducesResponseType(typeof(DeploymentDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<DeploymentDto[]>> ListDeployments(Guid projectId, [FromQuery] int limit = 20)
            => Ok(await mediator.Send(new ListDeploymentsQuery(projectId, limit), HttpContext.RequestAborted));

        /// <summary>All queued/running deployments plus the latest finished ones, across projects.</summary>
        [HttpGet("Deployments/Tasks", Name = "ListDeploymentTasks")]
        [ProducesResponseType(typeof(DeploymentTaskDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<DeploymentTaskDto[]>> ListDeploymentTasks([FromQuery] int recent = 8)
            => Ok(await mediator.Send(new ListDeploymentTasksQuery(recent), HttpContext.RequestAborted));

        /// <summary>One deployment including its full log.</summary>
        [HttpGet("Deployments/{deploymentId:guid}", Name = "GetDeployment")]
        [ProducesResponseType(typeof(DeploymentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeploymentDto>> GetDeployment(Guid deploymentId)
            => Ok(await mediator.Send(new GetDeploymentQuery(deploymentId), HttpContext.RequestAborted));

        /// <summary>Where to point a git provider's push webhook for this project, and its secret.</summary>
        [HttpGet("{projectId:guid}/Webhook", Name = "GetWebhook")]
        [ProducesResponseType(typeof(WebhookInfoDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<WebhookInfoDto>> GetWebhook(Guid projectId)
        {
            var project = await db.ProjectInfo.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, HttpContext.RequestAborted);
            if (project is null) return NotFound();

            var baseUrl = (configuration["DockiUp:PublicUrl"] ?? Environment.GetEnvironmentVariable("PUBLIC_URL")
                ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
            return Ok(new WebhookInfoDto($"{baseUrl}/api/Webhook/{project.Id}", project.WebhookSecret, project.Branch));
        }

        // ---- New project flow (source -> where & environment -> updates -> review) ----

        /// <summary>Lists a public git repo's branches and the compose files on a branch, with their services.</summary>
        [HttpPost("InspectRepository", Name = "InspectRepository")]
        [ProducesResponseType(typeof(RepositoryInspectionDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<RepositoryInspectionDto>> InspectRepository([FromBody] InspectRepositoryRequest request)
            => Ok(await mediator.Send(new InspectRepositoryQuery(request), HttpContext.RequestAborted));

        /// <summary>Converts a `docker run` command into a compose file; unsupported flags come back as warnings.</summary>
        [HttpPost("ConvertDockerRun", Name = "ConvertDockerRun")]
        [ProducesResponseType(typeof(GeneratedComposeDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<GeneratedComposeDto> ConvertDockerRun([FromBody] ConvertDockerRunRequest request)
            => Ok(DockiUp.Application.Compose.DockerRunConverter.Convert(request.Command ?? ""));

        /// <summary>Generates a one-service compose file from an image, ports, volumes, env and restart policy.</summary>
        [HttpPost("GenerateImageCompose", Name = "GenerateImageCompose")]
        [ProducesResponseType(typeof(GeneratedComposeDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<GeneratedComposeDto> GenerateImageCompose([FromBody] ImageProjectDto request)
            => Ok(DockiUp.Application.Compose.ImageComposeGenerator.Generate(request));

        /// <summary>Runs `docker compose config` on the target host/node; returns errors or the resolved services.</summary>
        [HttpPost("ValidateCompose", Name = "ValidateCompose")]
        [ProducesResponseType(typeof(ComposeValidationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ComposeValidationDto>> ValidateCompose([FromBody] ComposeValidationRequest request)
            => Ok(await mediator.Send(new ValidateComposeQuery(request), HttpContext.RequestAborted));

        /// <summary>Adopts a compose project already running on a host, in place (nothing moved or restarted).</summary>
        [HttpPost("AdoptProject", Name = "AdoptProject")]
        [ProducesResponseType(typeof(AdoptedProjectDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AdoptedProjectDto>> AdoptProject([FromBody] AdoptProjectDto request)
            => Ok(await mediator.Send(new AdoptProjectCommand(request), HttpContext.RequestAborted));
    }

    public record WebhookInfoDto(string Url, string Secret, string? Branch);

    public record PullResultDto(string Output);
}
