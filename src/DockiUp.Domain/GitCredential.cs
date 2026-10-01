namespace DockiUp.Domain
{
    /// <summary>HTTPS credentials for private git repositories (a GitHub/GitLab/Gitea personal access token).
    /// The token is stored vault-encrypted and never returned by the API.</summary>
    public class GitCredential : BaseEntity
    {
        public required string Name { get; set; }
        public required string Username { get; set; }
        public required string TokenEncrypted { get; set; }
    }
}
