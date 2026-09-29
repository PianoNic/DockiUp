using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using LibGit2Sharp;
using System.Text;

namespace DockiUp.Infrastructure.Services
{
    public class DockiUpProjectConfigurationService : IDockiUpProjectConfigurationService
    {
        private const string ComposeFileName = "dockiup_compose.yml";

        public Task<string> CloneRepositoryAsync(string projectPath, string gitUrl, string? branch = null) => Task.Run(() =>
        {
            Repository.Clone(gitUrl, projectPath, new CloneOptions { BranchName = string.IsNullOrWhiteSpace(branch) ? null : branch });
            using var repo = new Repository(projectPath);
            return repo.Head.FriendlyName;
        });

        // ponytail: public repos only - private repos need credentials (CloneOptions/FetchOptions.CredentialsProvider).
        public Task<GitSyncResult> SyncRepositoryAsync(string projectPath, string? branch, string? commit, Func<string, Task> log) => Task.Run(async () =>
        {
            using var repo = new Repository(projectPath);
            var target = string.IsNullOrWhiteSpace(branch) ? repo.Head.FriendlyName : branch;
            var before = repo.Head.Tip?.Sha;

            await log($"$ git fetch origin ({target})");
            try
            {
                Commands.Fetch(repo, "origin", Array.Empty<string>(), new FetchOptions { Prune = true }, null);
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

        private static string Short(string? sha) => sha is null ? "(none)" : sha[..Math.Min(7, sha.Length)];
    }
}
