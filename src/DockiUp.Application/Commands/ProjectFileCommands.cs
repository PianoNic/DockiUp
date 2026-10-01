using System.Text;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Commands
{
    /// <summary>Lists one folder of a DockiUp project (the project folder when <paramref name="Path"/> is empty).</summary>
    public sealed record ListProjectFilesQuery(Guid ProjectId, string? Path) : IRequest<ProjectFilesDto>;

    public sealed record ReadProjectFileQuery(Guid ProjectId, string Path) : IRequest<ProjectFileContentDto>;

    public sealed record DownloadProjectFileQuery(Guid ProjectId, string Path) : IRequest<byte[]>;

    /// <summary>Writes a file (editor save or upload). The compose file is validated first; in a git project
    /// a tracked file is committed and pushed so the next sync keeps it.</summary>
    public sealed record SaveProjectFileCommand(Guid ProjectId, string Path, byte[] Content, string? ActorName = null) : IRequest<ProjectFileWriteResult>;

    public sealed record DeleteProjectFileCommand(Guid ProjectId, string Path) : IRequest;

    public sealed record CreateProjectFolderCommand(Guid ProjectId, string Path) : IRequest;

    public sealed class ProjectFileHandlers(IDockerServiceResolver dockerResolver, IDockiUpDbContext db, IActivityLogger activity, Git.IGitCredentialsProvider? gitCredentials = null) :
        IRequestHandler<ListProjectFilesQuery, ProjectFilesDto>,
        IRequestHandler<ReadProjectFileQuery, ProjectFileContentDto>,
        IRequestHandler<DownloadProjectFileQuery, byte[]>,
        IRequestHandler<SaveProjectFileCommand, ProjectFileWriteResult>,
        IRequestHandler<DeleteProjectFileCommand>,
        IRequestHandler<CreateProjectFolderCommand>
    {
        public async ValueTask<ProjectFilesDto> Handle(ListProjectFilesQuery request, CancellationToken cancellationToken)
        {
            var project = await FindAsync(request.ProjectId, cancellationToken);
            var entries = await Docker(project).ListProjectFilesAsync(project.ProjectPath, Normalize(request.Path), cancellationToken);
            return new ProjectFilesDto(Normalize(request.Path), project.ProjectOrigin == ProjectOriginType.Git, ComposeFile(project), entries);
        }

        public async ValueTask<ProjectFileContentDto> Handle(ReadProjectFileQuery request, CancellationToken cancellationToken)
        {
            var project = await FindAsync(request.ProjectId, cancellationToken);
            return await Docker(project).ReadProjectFileAsync(project.ProjectPath, Normalize(request.Path), cancellationToken);
        }

        public async ValueTask<byte[]> Handle(DownloadProjectFileQuery request, CancellationToken cancellationToken)
        {
            var project = await FindAsync(request.ProjectId, cancellationToken);
            return await Docker(project).DownloadProjectFileAsync(project.ProjectPath, Normalize(request.Path), cancellationToken);
        }

        public async ValueTask<ProjectFileWriteResult> Handle(SaveProjectFileCommand request, CancellationToken cancellationToken)
        {
            var project = await FindAsync(request.ProjectId, cancellationToken);
            var docker = Docker(project);
            var path = Normalize(request.Path);

            // A broken compose file would only surface at the next deploy; refuse it while the user is still here.
            if (path == ComposeFile(project))
            {
                var validation = await docker.ValidateProjectComposeAsync(
                    new ComposeTarget(project.ProjectPath, project.ComposePath, project.DockerProjectName),
                    Encoding.UTF8.GetString(request.Content), cancellationToken);
                if (!validation.Valid)
                    throw new ArgumentException("The compose file is invalid: " + string.Join(Environment.NewLine, validation.Errors));
            }

            var commit = project.ProjectOrigin == ProjectOriginType.Git
                ? new ProjectFileCommit($"{request.ActorName ?? "DockiUp"}: update {path}", request.ActorName ?? "DockiUp",
                    gitCredentials is null ? null : await gitCredentials.GetAsync(project.GitCredentialId, cancellationToken))
                : null;
            var result = await docker.WriteProjectFileAsync(project.ProjectPath, path, request.Content, commit, cancellationToken);
            var details = result.Commit is { } sha ? $"{path} @ {sha[..Math.Min(7, sha.Length)]}" : path;
            await activity.LogAsync("file.save", project.ProjectName, project.Id, details, cancellationToken, request.ActorName);
            return result;
        }

        public async ValueTask<Unit> Handle(DeleteProjectFileCommand request, CancellationToken cancellationToken)
        {
            var project = await FindAsync(request.ProjectId, cancellationToken);
            var path = Normalize(request.Path);
            if (path == ComposeFile(project))
                throw new ArgumentException("The compose file can't be deleted; the project needs it to deploy.");
            await Docker(project).DeleteProjectFileAsync(project.ProjectPath, path, cancellationToken);
            await activity.LogAsync("file.delete", project.ProjectName, project.Id, path, cancellationToken);
            return default;
        }

        public async ValueTask<Unit> Handle(CreateProjectFolderCommand request, CancellationToken cancellationToken)
        {
            var project = await FindAsync(request.ProjectId, cancellationToken);
            await Docker(project).CreateProjectFolderAsync(project.ProjectPath, Normalize(request.Path), cancellationToken);
            return default;
        }

        private IDockerService Docker(ProjectInfo project) => dockerResolver.Resolve(project.NodeId);

        private async Task<ProjectInfo> FindAsync(Guid id, CancellationToken cancellationToken)
            => await db.ProjectInfo.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException($"Project with id {id} not found.");

        // Leading slashes are left in: the host refuses them like any absolute path.
        private static string Normalize(string? path) => (path ?? "").Replace('\\', '/').TrimEnd('/');

        /// <summary>The compose file relative to the project folder. Plain string work: the paths are the
        /// host's (a node may run another OS than the control plane).</summary>
        public static string? ComposeFile(ProjectInfo project)
        {
            var root = project.ProjectPath.TrimEnd('/', '\\');
            return project.ComposePath.StartsWith(root, StringComparison.Ordinal) && project.ComposePath.Length > root.Length
                ? Normalize(project.ComposePath[root.Length..]).TrimStart('/')
                : null;
        }
    }
}
