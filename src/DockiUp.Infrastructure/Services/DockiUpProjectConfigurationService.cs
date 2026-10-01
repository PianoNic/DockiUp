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
                        $"Could not read the repository (unreachable, misspelled, or private - for a private repository pick a git credential): {ex.Message}", ex);
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

        #region Commit edits back (#66)
        public IReadOnlySet<string> GetTrackedFiles(string projectPath)
        {
            if (!Repository.IsValid(projectPath)) return new HashSet<string>();
            using var repo = new Repository(projectPath);
            return repo.Index.Select(e => e.Path.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        }

        public Task<string> CommitAndPushFileAsync(string projectPath, string relativePath, string message, string authorName, GitCredentials? credentials = null) => Task.Run(() =>
        {
            using var repo = new Repository(projectPath);
            if (repo.Info.IsHeadDetached)
                throw new ArgumentException("The checkout is not on a branch, so the edit can't be committed. Deploy the project first.");

            var parent = repo.Head.Tip;
            Commands.Stage(repo, relativePath);
            var signature = new Signature(authorName, "dockiup@localhost", DateTimeOffset.UtcNow);
            Commit commit;
            try
            {
                commit = repo.Commit(message, signature, signature);
            }
            catch (EmptyCommitException)
            {
                return parent.Sha; // saved without changes (possibly only line endings): nothing to push
            }

            try
            {
                Push(repo, CredentialsProvider(credentials));
            }
            catch (Exception ex)
            {
                // Undo the local commit (index too) so the checkout matches origin again; the caller restores the file.
                repo.Reset(ResetMode.Mixed, parent);
                var url = repo.Network.Remotes["origin"]?.Url;
                // A server reports a rejected ref via OnPushStatusError; libgit2 itself refuses a non-fast-forward
                // push up front ("...contains commits that are not present locally").
                var rejected = ex is RejectedPushException || ex.Message.Contains("not present locally", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("fast-forward", StringComparison.OrdinalIgnoreCase);
                var reason = ex.Message.TrimEnd('.');
                throw new ArgumentException(rejected
                    ? $"Pushing to {url} was rejected ({reason}). The branch has moved on upstream: deploy to sync, then edit again."
                    : $"Could not push the change to {url}: {reason}. Committing edits back to git needs write access to the repository - add credentials for it, or edit the file in the repository instead.", ex);
            }
            return commit.Sha;
        });

        // Without credentials only remotes that need no auth (local paths, open servers) accept the push.
        private static void Push(Repository repo, LibGit2Sharp.Handlers.CredentialsHandler? credentials = null)
        {
            string? rejected = null;
            var options = new PushOptions
            {
                CredentialsProvider = credentials,
                // libgit2 reports a rejected ref (non-fast-forward, protected branch) here, not as an exception.
                OnPushStatusError = error => rejected = error.Message,
            };
            var branch = repo.Head.CanonicalName;
            repo.Network.Push(repo.Network.Remotes["origin"], $"{branch}:{branch}", options);
            if (rejected is not null) throw new RejectedPushException(rejected);
        }

        private sealed class RejectedPushException(string message) : Exception(message);
        #endregion
    }
}
