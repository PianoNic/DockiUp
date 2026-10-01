using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain.Enums;

namespace DockiUp.Application
{
    /// <summary>Lays a new project's files down under the projects root - on the server or on a node -
    /// before its first deploy: clone the repo (git) or write the UI compose file (compose), plus an optional .env.</summary>
    public static class ProjectPreparer
    {
        public const string DefaultRepoComposeFile = "docker-compose.yml";

        public static async Task<PreparedProject> PrepareAsync(SetupProjectDto dto, string projectsRoot, IDockiUpProjectConfigurationService files, Git.GitCredentials? credentials = null)
        {
            var projectPath = Path.GetFullPath(Path.Combine(projectsRoot, dto.ProjectName));
            if (Directory.Exists(projectPath) && Directory.EnumerateFileSystemEntries(projectPath).Any())
                throw new ArgumentException($"A project folder named '{dto.ProjectName}' already exists.");
            Directory.CreateDirectory(projectPath);

            try
            {
                return await PrepareFilesAsync(dto, projectPath, files, credentials);
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

        private static async Task<PreparedProject> PrepareFilesAsync(SetupProjectDto dto, string projectPath, IDockiUpProjectConfigurationService files, Git.GitCredentials? credentials)
        {
            PreparedProject prepared;
            if (dto.ProjectOrigin != ProjectOriginType.Git)
            {
                prepared = new PreparedProject(projectPath, await files.WriteComposeFileAsync(projectPath, dto.Compose!), null);
            }
            else
            {
                var branch = await files.CloneRepositoryAsync(projectPath, dto.GitUrl!, dto.Branch, credentials);
                var composeFile = string.IsNullOrWhiteSpace(dto.ComposeFile) ? DefaultRepoComposeFile : dto.ComposeFile.Trim();
                var composePath = ResolveInside(projectPath, composeFile, "The compose file must be inside the repository.");
                if (!File.Exists(composePath))
                    throw new ArgumentException($"'{composeFile}' was not found in the repository (branch {branch}).");
                prepared = new PreparedProject(projectPath, composePath, branch);
            }

            if (!string.IsNullOrEmpty(dto.EnvFile))
            {
                var envPath = EnvFilePath(prepared.ComposePath);
                // A tracked .env would be reset by the next git sync, silently dropping these values.
                if (dto.ProjectOrigin == ProjectOriginType.Git && File.Exists(envPath))
                    throw new ArgumentException("The repository already has a .env next to the compose file; edit that one in the repository instead.");
                await File.WriteAllTextAsync(envPath, dto.EnvFile);
            }
            return prepared;
        }

        /// <summary>Where a project's .env lives: next to its (first) compose file, which is where compose
        /// itself looks for it.</summary>
        public static string EnvFilePath(string composePath)
            => Path.Combine(Path.GetDirectoryName(composePath.Split(',')[0].Trim())!, EnvFileName);

        public const string EnvFileName = ".env";

        /// <summary>Resolves <paramref name="relative"/> under <paramref name="root"/>, refusing anything that
        /// would land outside it (absolute paths, '..').</summary>
        public static string ResolveInside(string root, string relative, string error)
        {
            var full = Path.GetFullPath(Path.Combine(root, relative));
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.Ordinal))
                throw new ArgumentException(error);
            return full;
        }
    }
}
