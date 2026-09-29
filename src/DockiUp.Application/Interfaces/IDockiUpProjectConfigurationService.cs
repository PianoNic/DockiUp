namespace DockiUp.Application.Interfaces
{
    /// <summary>Filesystem + git side of a project. Path-based so it runs identically on the server or a
    /// node, neither of which needs a database for it.</summary>
    public interface IDockiUpProjectConfigurationService
    {
        Task<string> WriteComposeFileAsync(string projectPath, string composeContent);

        /// <summary>Clones <paramref name="gitUrl"/> (optionally a specific branch); returns the checked-out branch.</summary>
        Task<string> CloneRepositoryAsync(string projectPath, string gitUrl, string? branch = null);

        /// <summary>Fetches and hard-resets the checkout to origin/<paramref name="branch"/> (current branch
        /// when null), or to <paramref name="commit"/> when given (redeploying an earlier version).
        /// Untracked files - like a UI-written compose file - are kept.</summary>
        Task<Dtos.GitSyncResult> SyncRepositoryAsync(string projectPath, string? branch, string? commit, Func<string, Task> log);
    }
}
