using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Validators;
using DockiUp.Application.Validators.Helpers;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Commands
{
    /// <summary>Lists a git repo's branches and compose files, without creating anything. Runs on the
    /// control plane: it only reads the repo.</summary>
    public sealed record InspectRepositoryQuery(InspectRepositoryRequest Request) : IRequest<RepositoryInspectionDto>;

    public sealed class InspectRepositoryQueryHandler(IDockiUpProjectConfigurationService files, Git.IGitCredentialsProvider? gitCredentials = null)
        : IRequestHandler<InspectRepositoryQuery, RepositoryInspectionDto>
    {
        public async ValueTask<RepositoryInspectionDto> Handle(InspectRepositoryQuery request, CancellationToken cancellationToken)
            => await files.InspectRepositoryAsync(request.Request.GitUrl.Trim(), string.IsNullOrWhiteSpace(request.Request.Branch) ? null : request.Request.Branch.Trim(), cancellationToken,
                gitCredentials is null ? null : await gitCredentials.GetAsync(request.Request.GitCredentialId, cancellationToken));
    }

    public sealed class InspectRepositoryQueryValidator : AbstractValidator<InspectRepositoryQuery>
    {
        public InspectRepositoryQueryValidator()
        {
            RuleFor(q => q.Request.GitUrl)
                .NotEmpty().WithMessage("Git URL is required.")
                .MaximumLength(2000).WithMessage("Git URL must not exceed 2000 characters.");
            RuleFor(q => q.Request.Branch)
                .MaximumLength(250).WithMessage("Branch must not exceed 250 characters.");
        }
    }

    /// <summary>Validates compose with `docker compose config` on the host the project will run on.</summary>
    public sealed record ValidateComposeQuery(ComposeValidationRequest Request) : IRequest<ComposeValidationDto>;

    public sealed class ValidateComposeQueryHandler(IDockerServiceResolver dockerResolver, Git.IGitCredentialsProvider? gitCredentials = null)
        : IRequestHandler<ValidateComposeQuery, ComposeValidationDto>
    {
        public async ValueTask<ComposeValidationDto> Handle(ValidateComposeQuery request, CancellationToken cancellationToken)
        {
            // Resolved here from the stored id; never part of what a client sends.
            var credentials = gitCredentials is null ? null : await gitCredentials.GetAsync(request.Request.GitCredentialId, cancellationToken);
            return await dockerResolver.Resolve(request.Request.NodeId).ValidateComposeAsync(request.Request, credentials, cancellationToken);
        }
    }

    public sealed class ValidateComposeQueryValidator : AbstractValidator<ValidateComposeQuery>
    {
        public ValidateComposeQueryValidator()
        {
            RuleFor(q => q.Request)
                .Must(r => !string.IsNullOrWhiteSpace(r.Compose) || !string.IsNullOrWhiteSpace(r.GitUrl))
                .WithMessage("Provide compose content or a git repository.");
            RuleFor(q => q.Request.Compose)
                .MaximumLength(DeployProjectCommandValidator.MaxComposeLength)
                .WithMessage($"Compose content must not exceed {DeployProjectCommandValidator.MaxComposeLength} characters.");
            RuleFor(q => q.Request.EnvFile)
                .MaximumLength(DeployProjectCommandValidator.MaxEnvFileLength)
                .WithMessage($".env content must not exceed {DeployProjectCommandValidator.MaxEnvFileLength} characters.");
            RuleFor(q => q.Request.GitUrl).MaximumLength(2000).WithMessage("Git URL must not exceed 2000 characters.");
            RuleFor(q => q.Request.ComposeFile)
                .Must(f => f is null || (!Path.IsPathRooted(f) && !f.Split('/', '\\').Contains("..")))
                .WithMessage("Compose file must be a relative path inside the repository.");
        }
    }

    /// <summary>Registers every compose project on the local host and the online nodes that DockiUp doesn't know yet,
    /// exactly like a manual adopt (nothing moved, written or restarted). Skipped: DockiUp's own project, projects
    /// whose compose files DockiUp can't read (deploys would fail), and a name already registered on any host.
    /// Returns how many were adopted.</summary>
    public sealed record AutoAdoptCommand : IRequest<int>;

    public sealed class AutoAdoptCommandHandler(IDockerServiceResolver dockerResolver, INodeDirectory nodes, IDockiUpDbContext db, IActivityLogger activity)
        : IRequestHandler<AutoAdoptCommand, int>
    {
        public async ValueTask<int> Handle(AutoAdoptCommand request, CancellationToken cancellationToken)
        {
            // ponytail: one name per DockiUp — the same stack name on a second host is left for a manual adopt
            // (this also keeps a node that shares the local daemon from adopting everything twice).
            var known = (await db.ProjectInfo.Select(p => p.DockerProjectName).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var adopted = 0;

            foreach (var nodeId in new Guid?[] { null }.Concat(nodes.GetOnlineNodeIds().Select(id => (Guid?)id)))
            {
                ProjectDto[] projects;
                try
                {
                    projects = await dockerResolver.Resolve(nodeId).GetRawProjectsAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    continue; // a node dropping out mid-scan is picked up on the next one
                }

                foreach (var p in projects.Where(p => !p.IsDockiUp && p.ComposeFilesReachable
                    && !string.IsNullOrWhiteSpace(p.ComposeWorkingDir) && !string.IsNullOrWhiteSpace(p.ComposeConfigFiles)))
                {
                    if (!known.Add(p.DockerProjectName)) continue;
                    var project = new ProjectInfo
                    {
                        ProjectName = p.DockerProjectName,
                        DockerProjectName = p.DockerProjectName,
                        ProjectOrigin = ProjectOriginType.Adopted,
                        NodeId = nodeId,
                        ProjectPath = p.ComposeWorkingDir!,
                        ComposePath = p.ComposeConfigFiles!,
                        ProjectUpdateMethod = ProjectUpdateMethod.Manual,
                    };
                    db.ProjectInfo.Add(project);
                    await db.SaveChangesAsync(cancellationToken);
                    await activity.LogAsync("adopt", project.ProjectName, project.Id, "automatically", cancellationToken);
                    adopted++;
                }
            }
            return adopted;
        }
    }

    /// <summary>Takes over a compose project that already runs on a host: stores it with the working dir and
    /// compose files compose recorded in its container labels. Nothing is moved, written or restarted.</summary>
    public sealed record AdoptProjectCommand(AdoptProjectDto Dto) : IRequest<AdoptedProjectDto>;

    public sealed class AdoptProjectCommandHandler(IDockerServiceResolver dockerResolver, IDockiUpDbContext db, IActivityLogger activity)
        : IRequestHandler<AdoptProjectCommand, AdoptedProjectDto>
    {
        public async ValueTask<AdoptedProjectDto> Handle(AdoptProjectCommand request, CancellationToken cancellationToken)
        {
            var dto = request.Dto;
            var running = (await dockerResolver.Resolve(dto.NodeId).GetRawProjectsAsync())
                .FirstOrDefault(p => string.Equals(p.DockerProjectName, dto.DockerProjectName, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"No compose project named '{dto.DockerProjectName}' runs on that host.");

            if (await db.ProjectInfo.AnyAsync(p => p.DockerProjectName == running.DockerProjectName && p.NodeId == dto.NodeId, cancellationToken))
                throw new ArgumentException($"'{running.DockerProjectName}' is already managed by DockiUp.");
            if (string.IsNullOrWhiteSpace(running.ComposeWorkingDir) || string.IsNullOrWhiteSpace(running.ComposeConfigFiles))
                throw new ArgumentException($"'{running.DockerProjectName}' has no compose file labels (not started with docker compose v2?), so DockiUp can't tell where its files are.");

            var project = new ProjectInfo
            {
                ProjectName = running.DockerProjectName,
                DockerProjectName = running.DockerProjectName,
                Description = dto.Description,
                ProjectOrigin = ProjectOriginType.Adopted,
                NodeId = dto.NodeId,
                ProjectPath = running.ComposeWorkingDir,
                ComposePath = running.ComposeConfigFiles,
                ProjectUpdateMethod = dto.ProjectUpdateMethod,
                PeriodicIntervalInMinutes = dto.ProjectUpdateMethod == ProjectUpdateMethod.Periodically ? dto.PeriodicIntervalInMinutes : null,
            };
            db.ProjectInfo.Add(project);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("adopt", project.ProjectName, project.Id, running.ComposeWorkingDir, cancellationToken);
            return new AdoptedProjectDto(project.Id, project.DockerProjectName);
        }
    }

    public sealed class AdoptProjectCommandValidator : AbstractValidator<AdoptProjectCommand>
    {
        public AdoptProjectCommandValidator()
        {
            RuleFor(c => c.Dto.DockerProjectName)
                .NotEmpty().WithMessage("Pick the project to adopt.")
                .MaximumLength(200).WithMessage("Project name must not exceed 200 characters.");
            RuleFor(c => c.Dto.Description)
                .MaximumLength(500).WithMessage("Description must not exceed 500 characters.");
            RuleFor(c => c.Dto.ProjectUpdateMethod)
                .IsValidEnum().WithMessage("Invalid project update method.");
            RuleFor(c => c.Dto.PeriodicIntervalInMinutes)
                .NotNull().GreaterThan(0).WithMessage("Periodic interval must be greater than 0.")
                .When(c => c.Dto.ProjectUpdateMethod == ProjectUpdateMethod.Periodically);
        }
    }
}
