using DockiUp.Application.Dtos;
using DockiUp.Application.Git;
using DockiUp.Application.Interfaces;
using LibGit2Sharp;
using System.Text;

namespace DockiUp.Infrastructure.Services
{
    public class DockiUpProjectConfigurationService : IDockiUpProjectConfigurationService
    {
        private const string ComposeFileName = "dockiup_compose.yml";

        public Task<string> CloneRepositoryAsync(string projectPath, string gitUrl, string? branch = null, GitCredentials? credentials = null) => Task.Run(() =>
        {
            var options = new CloneOptions { BranchName = string.IsNullOrWhiteSpace(branch) ? null : branch };
            options.FetchOptions.CredentialsProvider = CredentialsProvider(credentials);
            Repository.Clone(gitUrl, projectPath, options);
            using var repo = new Repository(projectPath);
            return repo.Head.FriendlyName;
        });

        public Task<GitSyncResult> SyncRepositoryAsync(string projectPath, string? branch, string? commit, Func<string, Task> log, GitCredentials? credentials = null) => Task.Run(async () =>
        {
            using var repo = new Repository(projectPath);
            var target = string.IsNullOrWhiteSpace(branch) ? repo.Head.FriendlyName : branch;
            var before = repo.Head.Tip?.Sha;

            await log($"$ git fetch origin ({target})");
            try
            {
                Commands.Fetch(repo, "origin", Array.Empty<string>(), new FetchOptions { Prune = true, CredentialsProvider = CredentialsProvider(credentials) }, null);
            }
            catch (LibGit2SharpException ex)
            {
                // libgit2's own messages ("unsupported URL protocol") don't say which remote; name it.
                throw new InvalidOperationException($"Could not fetch from origin ({repo.Network.Remotes["origin"]?.Url}): {ex.Message}", ex);
            }

            var remote = repo.Branches[$"origin/{target}"]
                ?? throw new InvalidOperationException($"Branch '{target}' does not exist on origin.");

            // Put the local branch on the remote tip, whatever happened locally: the remote is the source
            // of truth (a merge could conflict and silently leave the old code running).
            var local = repo.Branches[target] ?? repo.CreateBranch(target, remote.Tip);
            repo.Branches.Update(local, b => b.TrackedBranch = remote.CanonicalName);
            Commands.Checkout(repo, local, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
            var tip = remote.Tip;
            if (!string.IsNullOrWhiteSpace(commit))
            {
                tip = repo.Lookup<Commit>(commit)
                    ?? throw new InvalidOperationException($"Commit {Short(commit)} is not in the repository (force-pushed away?).");
                await log($"Pinned to requested version {Short(tip.Sha)}.");
            }
            repo.Reset(ResetMode.Hard, tip);

            var after = repo.Head.Tip.Sha;
            await log(before == after
                ? $"Already at {Short(after)} - no new commits."
                : $"Updated {Short(before)} -> {Short(after)}: {tip.MessageShort}");
            return new GitSyncResult(before, after, target, tip.MessageShort);
        });

        public async Task<string> WriteComposeFileAsync(string projectPath, string composeContent)
        {
            string filePath = Path.Combine(projectPath, ComposeFileName);
            await File.WriteAllTextAsync(filePath, composeContent, Encoding.UTF8);
            return filePath;
        }

        /// <summary>libgit2 asks for credentials only when the remote demands them, so public repos are
        /// unaffected. Clone, fetch and push all take this; null means anonymous.</summary>
        public static LibGit2Sharp.Handlers.CredentialsHandler? CredentialsProvider(GitCredentials? credentials)
            => credentials is null
                ? null
                : (_, _, _) => new UsernamePasswordCredentials
                {
                    // Token-based HTTPS auth ignores the user name on GitHub/Gitea, but it must not be empty.
                    Username = string.IsNullOrWhiteSpace(credentials.Username) ? "git" : credentials.Username,
                    Password = credentials.Token,
                };

        private static string Short(string? sha) => sha is null ? "(none)" : sha[..Math.Min(7, sha.Length)];

        public Task<RepositoryInspectionDto> InspectRepositoryAsync(string gitUrl, string? branch, CancellationToken cancellationToken = default, GitCredentials? credentials = null) => Task.Run(() =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "dockiup-inspect-" + Guid.NewGuid().ToString("N"));
            try
            {
                try
                {
                    Repository.Clone(gitUrl, dir, InspectCloneOptions(branch, credentials));
                }
                catch (LibGit2SharpException ex)
                {
                    throw new ArgumentException(
                        $"Could not read the repository (unreachable, misspelled, or private - only public repositories are supported for now): {ex.Message}", ex);
                }

                using var repo = new Repository(dir);
                const string remotePrefix = "origin/";
                var branches = repo.Branches
                    .Where(b => b.IsRemote && b.FriendlyName.StartsWith(remotePrefix) && b.FriendlyName != "origin/HEAD")
                    .Select(b => b.FriendlyName[remotePrefix.Length..])
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                // origin/HEAD names the remote's default branch; without it, an unpinned clone is on it anyway.
                var defaultBranch = (repo.Refs["refs/remotes/origin/HEAD"] as SymbolicReference)?.Target.CanonicalName
                    .Replace("refs/remotes/origin/", "")
                    ?? (branch is null ? repo.Head.FriendlyName : null);

                var composeFiles = DockiUp.Application.Compose.ComposeFiles.Find(dir);
                return new RepositoryInspectionDto(repo.Head.FriendlyName, defaultBranch, branches, composeFiles,
                    DockiUp.Application.Compose.ComposeFiles.DefaultFile(composeFiles.Select(f => f.Path).ToArray()));
            }
            finally
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                            File.SetAttributes(file, FileAttributes.Normal); // pack files are read-only
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch { /* best effort: it's a temp folder */ }
            }
        }, cancellationToken);

        // The one place inspection builds its clone options, including credentials for private repos.
        private static CloneOptions InspectCloneOptions(string? branch, GitCredentials? credentials)
        {
            var options = new CloneOptions { BranchName = string.IsNullOrWhiteSpace(branch) ? null : branch };
            options.FetchOptions.CredentialsProvider = CredentialsProvider(credentials);
            return options;
        }
    }
}
