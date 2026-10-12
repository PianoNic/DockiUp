using System.Text.Json.Serialization;
using DockiUp.Application.Deployments;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Controllers
{
    /// <summary>A project's environment: plain variables, encrypted secrets, and variables filled from a shared
    /// vault secret. Written with the project's .env to the env file compose gets at deploy time. Secret values
    /// are write-only: they are never returned.</summary>
    [ApiController]
    [Route("api/Project/{projectId:guid}/Environment")]
    public class ProjectEnvironmentController(IDockiUpDbContext db, ISecretsVaultService vault, IActivityLogger activity) : ControllerBase
    {
        [JsonConverter(typeof(JsonStringEnumConverter<EnvironmentKind>))]
        public enum EnvironmentKind { Variable, Secret, Vault }

        /// <summary>Value: a variable's value (null for secrets). VaultSecret*: the shared secret a Vault entry reads.</summary>
        public record EnvironmentEntryDto(string Name, string? Group, EnvironmentKind Kind, string? Value, Guid? VaultSecretId, string? VaultSecretName);

        /// <summary>Value: the new value; for a Secret, null keeps the stored value of <paramref name="KeepFrom"/>
        /// (its name when loaded, so renaming keeps it). VaultSecretId: required for a Vault entry.</summary>
        public record EnvironmentEntryInput(string Name, string? Group, EnvironmentKind Kind, string? Value, string? KeepFrom, Guid? VaultSecretId);

        [HttpGet(Name = "GetProjectEnvironment")]
        [ProducesResponseType(typeof(EnvironmentEntryDto[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<EnvironmentEntryDto[]>> GetProjectEnvironment(Guid projectId, CancellationToken cancellationToken)
        {
            await FindAsync(projectId, cancellationToken);
            return Ok(await ListAsync(projectId, cancellationToken));
        }

        /// <summary>Replaces the whole environment. Applied on the next deploy.</summary>
        [HttpPut(Name = "SetProjectEnvironment")]
        [ProducesResponseType(typeof(EnvironmentEntryDto[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<EnvironmentEntryDto[]>> SetProjectEnvironment(Guid projectId, [FromBody] EnvironmentEntryInput[] entries, CancellationToken cancellationToken)
        {
            var project = await FindAsync(projectId, cancellationToken);
            var cleaned = entries.Select(e => e with { Name = (e.Name ?? "").Trim(), Group = string.IsNullOrWhiteSpace(e.Group) ? null : e.Group.Trim() }).ToArray();
            foreach (var e in cleaned)
                if (!EnvFile.IsValidName(e.Name))
                    throw new ArgumentException($"'{e.Name}' is not a valid variable name (letters, digits and _; not starting with a digit).");
            if (cleaned.GroupBy(e => e.Name).FirstOrDefault(g => g.Count() > 1) is { } dup)
                throw new ArgumentException($"{dup.Key} is defined more than once.");

            var stored = await db.ProjectVariables.Where(v => v.ProjectId == projectId).ToListAsync(cancellationToken);
            var storedSecrets = stored.Where(v => v.IsSecret).ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
            var vaultIds = cleaned.Where(e => e.Kind == EnvironmentKind.Vault).Select(e => e.VaultSecretId ?? Guid.Empty).Distinct().ToList();
            if (await db.Secrets.CountAsync(s => vaultIds.Contains(s.Id), cancellationToken) != vaultIds.Count)
                throw new ArgumentException("A selected vault secret no longer exists.");

            var variables = new List<ProjectVariable>();
            foreach (var e in cleaned.Where(e => e.Kind != EnvironmentKind.Vault))
            {
                var value = e.Kind == EnvironmentKind.Variable ? e.Value ?? ""
                    : e.Value is not null ? vault.Encrypt(e.Value)
                    : e.KeepFrom is not null && storedSecrets.TryGetValue(e.KeepFrom, out var kept) ? kept
                    : throw new ArgumentException($"The secret {e.Name} needs a value.");
                variables.Add(new ProjectVariable { ProjectId = projectId, Name = e.Name, Group = e.Group, IsSecret = e.Kind == EnvironmentKind.Secret, Value = value });
            }

            db.ProjectVariables.RemoveRange(stored);
            db.ProjectSecrets.RemoveRange(await db.ProjectSecrets.Where(m => m.ProjectId == projectId).ToListAsync(cancellationToken));
            db.ProjectVariables.AddRange(variables);
            db.ProjectSecrets.AddRange(cleaned.Where(e => e.Kind == EnvironmentKind.Vault)
                .Select(e => new ProjectSecret { ProjectId = projectId, EnvName = e.Name, SecretId = e.VaultSecretId!.Value, Group = e.Group }));
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("environment.update", project.ProjectName, project.Id,
                cleaned.Length == 0 ? "none" : string.Join(", ", cleaned.Select(e => e.Name)), cancellationToken);
            return Ok(await ListAsync(projectId, cancellationToken));
        }

        private async Task<ProjectInfo> FindAsync(Guid projectId, CancellationToken cancellationToken)
            => await db.ProjectInfo.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new KeyNotFoundException($"Project with id {projectId} not found.");

        private async Task<EnvironmentEntryDto[]> ListAsync(Guid projectId, CancellationToken cancellationToken)
        {
            var variables = await db.ProjectVariables.Where(v => v.ProjectId == projectId).ToListAsync(cancellationToken);
            var mapped = await db.ProjectSecrets.Where(m => m.ProjectId == projectId)
                .Join(db.Secrets, m => m.SecretId, s => s.Id, (m, s) => new EnvironmentEntryDto(m.EnvName, m.Group, EnvironmentKind.Vault, null, s.Id, s.Name))
                .ToListAsync(cancellationToken);
            return variables
                .Select(v => new EnvironmentEntryDto(v.Name, v.Group, v.IsSecret ? EnvironmentKind.Secret : EnvironmentKind.Variable, v.IsSecret ? null : v.Value, null, null))
                .Concat(mapped)
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
