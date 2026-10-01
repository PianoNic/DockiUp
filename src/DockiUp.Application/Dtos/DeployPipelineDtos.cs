namespace DockiUp.Application.Dtos
{
    /// <summary>Outcome of syncing a checkout to its remote branch (or a pinned commit). Before == After means nothing new.</summary>
    public record GitSyncResult(string? Before, string? After, string Branch, string? Message = null);

    /// <summary>What `docker compose up` needs: the folder, the compose file in it, and the project name.
    /// <paramref name="Services"/> limits pull/up to those services (image updates); null means all.</summary>
    public record ComposeTarget(string ProjectPath, string ComposePath, string DockerProjectName, string[]? Services = null);

    /// <summary>Changed is true when compose recreated, added or removed any container.</summary>
    public record ComposeUpResult(bool Changed);

    /// <summary>Files prepared for a new project (locally or on a node), before its first deploy.</summary>
    public record PreparedProject(string ProjectPath, string ComposePath, string? Branch);
}
