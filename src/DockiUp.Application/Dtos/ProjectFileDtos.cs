namespace DockiUp.Application.Dtos
{
    /// <summary>One entry of a project folder. <paramref name="Path"/> is relative to the project folder,
    /// '/'-separated. <paramref name="Tracked"/> is true for files committed in the project's git checkout.</summary>
    public record ProjectFileEntryDto(string Name, string Path, bool IsDirectory, long Size, DateTime ModifiedAt, bool Tracked);

    /// <summary>A text file's content (UTF-8, size-limited; binary files are refused).</summary>
    public record ProjectFileContentDto(string Path, string Content, long Size, DateTime ModifiedAt, bool Tracked);

    /// <summary>A folder listing plus what the UI needs to label it: whether edits are committed back to git
    /// and which file is the compose file (relative path).</summary>
    public record ProjectFilesDto(string Path, bool IsGit, string? ComposeFile, ProjectFileEntryDto[] Entries);

    /// <summary>Asks the host to commit a written file back to git (only happens when the file is tracked).</summary>
    /// <summary><paramref name="Credentials"/> are set by the server from the project's stored git credential
    /// (private repos); they reach a node only inside the RPC payload.</summary>
    public record ProjectFileCommit(string Message, string AuthorName, Git.GitCredentials? Credentials = null);

    /// <summary>Outcome of writing a file: the pushed commit when it was tracked in git, else null.</summary>
    public record ProjectFileWriteResult(bool Tracked, string? Commit);

    /// <summary>`docker compose config` outcome: errors when invalid, the service names when valid.</summary>
    public record ComposeValidationResult(bool Valid, string[] Errors, string[] Services);
}
