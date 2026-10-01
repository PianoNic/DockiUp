using DockiUp.Application.Deployments;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Controllers
{
    /// <summary>Per-project settings beyond the webhook: env vars filled from vault secrets at deploy time, and
    /// the git credential used to clone/fetch a private repository. Secret values never pass through here.</summary>
    [ApiController]
    [Route("api/Project/{projectId:guid}")]
    public class ProjectSettingsController(IDockiUpDbContext db, IActivityLogger activity) : ControllerBase
    {
        public record ProjectSecretDto(string EnvName, Guid SecretId, string SecretName);
        public record ProjectSettingsDto(bool IsGit, Guid? GitCredentialId, ProjectSecretDto[] Secrets);
        public record ProjectSecretMapping(string EnvName, Guid SecretId);
        public record SetProjectGitCredentialRequest(Guid? GitCredentialId);

        [HttpGet("Settings", Name = "GetProjectSettings")]
        [ProducesResponseType(typeof(ProjectSettingsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProjectSettingsDto>> GetProjectSettings(Guid projectId, CancellationToken cancellationToken)
            => Ok(await SettingsAsync(await FindAsync(projectId, cancellationToken), cancellationToken));

        /// <summary>Replaces the project's env-var -> vault-secret mappings. Applied on the next deploy.</summary>
        [HttpPut("Secrets", Name = "SetProjectSecrets")]
        [ProducesResponseType(typeof(ProjectSettingsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProjectSettingsDto>> SetProjectSecrets(Guid projectId, [FromBody] ProjectSecretMapping[] mappings, CancellationToken cancellationToken)
        {
            var project = await FindAsync(projectId, cancellationToken);
            var cleaned = mappings.Select(m => m with { EnvName = (m.EnvName ?? "").Trim() }).ToArray();
            foreach (var m in cleaned)
                if (!EnvFile.IsValidName(m.EnvName))
                    throw new ArgumentException($"'{m.EnvName}' is not a valid variable name (letters, digits and _; not starting with a digit).");
            if (cleaned.GroupBy(m => m.EnvName).FirstOrDefault(g => g.Count() > 1) is { } dup)
                throw new ArgumentException($"{dup.Key} is mapped more than once.");
            var secretIds = cleaned.Select(m => m.SecretId).Distinct().ToList();
            if (await db.Secrets.CountAsync(s => secretIds.Contains(s.Id), cancellationToken) != secretIds.Count)
                throw new ArgumentException("A selected vault secret no longer exists.");

            db.ProjectSecrets.RemoveRange(await db.ProjectSecrets.Where(m => m.ProjectId == projectId).ToListAsync(cancellationToken));
            db.ProjectSecrets.AddRange(cleaned.Select(m => new ProjectSecret { ProjectId = projectId, EnvName = m.EnvName, SecretId = m.SecretId }));
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("secrets.update", project.ProjectName, project.Id,
                cleaned.Length == 0 ? "none" : string.Join(", ", cleaned.Select(m => m.EnvName)), cancellationToken);
            return Ok(await SettingsAsync(project, cancellationToken));
        }

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
            return Ok(await SettingsAsync(project, cancellationToken));
        }

        private async Task<ProjectInfo> FindAsync(Guid projectId, CancellationToken cancellationToken)
            => await db.ProjectInfo.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new KeyNotFoundException($"Project with id {projectId} not found.");

        private async Task<ProjectSettingsDto> SettingsAsync(ProjectInfo project, CancellationToken cancellationToken)
        {
            var secrets = await db.ProjectSecrets.Where(m => m.ProjectId == project.Id)
                .Join(db.Secrets, m => m.SecretId, s => s.Id, (m, s) => new ProjectSecretDto(m.EnvName, s.Id, s.Name))
                .ToListAsync(cancellationToken);
            return new ProjectSettingsDto(project.ProjectOrigin == ProjectOriginType.Git, project.GitCredentialId,
                secrets.OrderBy(s => s.EnvName, StringComparer.Ordinal).ToArray());
        }
    }
}
