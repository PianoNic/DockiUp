using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Mediator;
using Microsoft.Extensions.Options;

namespace DockiUp.Application.Commands
{
    /// <summary>Creates a project: lays its files down (here or on its node), stores it, and queues the
    /// first deployment - which then runs, and is recorded, like every later one.</summary>
    public sealed class DeployProjectCommand : IRequest<DeploymentDto?>
    {
        public DeployProjectCommand(SetupProjectDto setupContainerDto)
        {
            SetupContainerDto = setupContainerDto;
        }

        public SetupProjectDto SetupContainerDto { get; set; }
    }

    public sealed class DeployProjectCommandHandler(
        IOptions<SystemPaths> systemPaths,
        IDockiUpProjectConfigurationService projectFiles,
        IDockiUpDbContext dbContext,
        IActivityLogger activityLogger,
        INodeRpc nodeRpc,
        IMediator mediator,
        Git.IGitCredentialsProvider? gitCredentials = null) : IRequestHandler<DeployProjectCommand, DeploymentDto?>
    {
        public async ValueTask<DeploymentDto?> Handle(DeployProjectCommand request, CancellationToken cancellationToken)
        {
            var dto = request.SetupContainerDto;

            // Import has nothing to write or start yet (parity with the previous handler).
            if (dto.ProjectOrigin == ProjectOriginType.Import)
            {
                await activityLogger.LogAsync("deploy", dto.ProjectName, details: dto.ProjectOrigin.ToString(), cancellationToken: cancellationToken);
                return null;
            }

            // Private repo: decrypted here, handed to the clone in-process or inside the node's RPC payload.
            var credentials = dto.ProjectOrigin == ProjectOriginType.Git && gitCredentials is not null
                ? await gitCredentials.GetAsync(dto.GitCredentialId, cancellationToken)
                : null;

            // The files live where the project will run: on its node (which has no database, so it just
            // reports back the paths it used), or on this host.
            var prepared = dto.NodeId is Guid nodeId
                ? await nodeRpc.InvokeAsync<PreparedProject>(nodeId, "PrepareProject", [dto, credentials], cancellationToken)
                : await ProjectPreparer.PrepareAsync(dto, systemPaths.Value.ProjectsPath, projectFiles, credentials);

            // The server always owns the project row, even for node-hosted projects.
            var projectInfo = new ProjectInfo
            {
                ProjectName = dto.ProjectName,
                DockerProjectName = new string(dto.ProjectName.ToLower().Where(c => !char.IsWhiteSpace(c)).ToArray()),
                Description = dto.Description,
                ProjectOrigin = dto.ProjectOrigin,
                GitUrl = dto.GitUrl,
                Branch = prepared.Branch,
                NodeId = dto.NodeId,
                ProjectPath = prepared.ProjectPath,
                ComposePath = prepared.ComposePath,
                ProjectUpdateMethod = dto.ProjectUpdateMethod,
                PeriodicIntervalInMinutes = dto.PeriodicIntervalInMinutes,
                GitCredentialId = credentials is null ? null : dto.GitCredentialId,
            };
            await dbContext.ProjectInfo.AddAsync(projectInfo, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await activityLogger.LogAsync("create", dto.ProjectName, projectInfo.Id, dto.ProjectOrigin.ToString(), cancellationToken);
            return await mediator.Send(new QueueDeploymentCommand(projectInfo.Id, DeploymentTrigger.Create), cancellationToken);
        }
    }
}
