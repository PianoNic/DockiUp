using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain.Enums;

namespace DockiUp.Application
{
    /// <summary>Lays a new project's files down under the projects root - on the server or on a node -
    /// before its first deploy: clone the repo (git) or write the UI compose file (compose).</summary>
    public static class ProjectPreparer
    {
        public const string DefaultRepoComposeFile = "docker-compose.yml";

        public static async Task<PreparedProject> PrepareAsync(SetupProjectDto dto, string projectsRoot, IDockiUpProjectConfigurationService files)
        {
            var projectPath = Path.GetFullPath(Path.Combine(projectsRoot, dto.ProjectName));
            if (Directory.Exists(projectPath) && Directory.EnumerateFileSystemEntries(projectPath).Any())
                throw new ArgumentException($"A project folder named '{dto.ProjectName}' already exists.");
            Directory.CreateDirectory(projectPath);

            try
            {
                return await PrepareFilesAsync(dto, projectPath, files);
            }
            catch
            {
                // Leave nothing behind, or a retry under the same name hits "folder already exists".
                try
                {
                    // git marks pack files read-only, which Directory.Delete refuses on Windows.
                    foreach (var file in Directory.EnumerateFiles(projectPath, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(projectPath, recursive: true);
                }
                catch { /* best effort */ }
                throw;
            }
        }

        private static async Task<PreparedProject> PrepareFilesAsync(SetupProjectDto dto, string projectPath, IDockiUpProjectConfigurationService files)
        {
            if (dto.ProjectOrigin != ProjectOriginType.Git)
                return new PreparedProject(projectPath, await files.WriteComposeFileAsync(projectPath, dto.Compose!), null);

            var branch = await files.CloneRepositoryAsync(projectPath, dto.GitUrl!, dto.Branch);
            var composeFile = string.IsNullOrWhiteSpace(dto.ComposeFile) ? DefaultRepoComposeFile : dto.ComposeFile.Trim();
            var composePath = Path.GetFullPath(Path.Combine(projectPath, composeFile));
            if (!composePath.StartsWith(projectPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("The compose file must be inside the repository.");
            if (!File.Exists(composePath))
                throw new ArgumentException($"'{composeFile}' was not found in the repository (branch {branch}).");
            return new PreparedProject(projectPath, composePath, branch);
        }
    }
}
