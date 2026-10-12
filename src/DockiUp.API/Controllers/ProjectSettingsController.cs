using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Controllers
{
    /// <summary>Per-project settings beyond the webhook and the environment (ProjectEnvironmentController): the git
    /// credential used to clone/fetch a private repository.</summary>
    [ApiController]
    [Route("api/Project/{projectId:guid}")]
    public class ProjectSettingsController(IDockiUpDbContext db, IActivityLogger activity) : ControllerBase
    {
        public record ProjectSettingsDto(bool IsGit, Guid? GitCredentialId);
        public record SetProjectGitCredentialRequest(Guid? GitCredentialId);

        [HttpGet("Settings", Name = "GetProjectSettings")]
        [ProducesResponseType(typeof(ProjectSettingsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProjectSettingsDto>> GetProjectSettings(Guid projectId, CancellationToken cancellationToken)
            => Ok(Settings(await FindAsync(projectId, cancellationToken)));

        /// <summary>Chooses the credential for a git project's repository (null: public repository).</summary>
        [HttpPut("GitCredential", Name = "SetProjectGitCredential")]
        [ProducesResponseType(typeof(ProjectSettingsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProjectSettingsDto>> SetProjectGitCredential(Guid projectId, [FromBody] SetProjectGitCredentialRequest request, CancellationToken cancellationToken)
        {
            var project = await FindAsync(projectId, cancellationToken);
            if (project.ProjectOrigin != ProjectOriginType.Git)
                throw new ArgumentException("Only git projects use a git credential.");
            var credential = request.GitCredentialId is { } id
                ? await db.GitCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                    ?? throw new ArgumentException("That git credential does not exist.")
                : null;
            project.GitCredentialId = credential?.Id;
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("git.credential", project.ProjectName, project.Id, credential?.Name ?? "none", cancellationToken);
            return Ok(Settings(project));
        }

        private async Task<ProjectInfo> FindAsync(Guid projectId, CancellationToken cancellationToken)
            => await db.ProjectInfo.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new KeyNotFoundException($"Project with id {projectId} not found.");

        private static ProjectSettingsDto Settings(ProjectInfo project)
            => new(project.ProjectOrigin == ProjectOriginType.Git, project.GitCredentialId);
    }
}
