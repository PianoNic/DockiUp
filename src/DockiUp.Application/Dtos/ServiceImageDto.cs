namespace DockiUp.Application.Dtos
{
    /// <summary>What one compose service of a project runs: the image reference from its config and the
    /// registry digests (`repo@sha256:...`) the local image was pulled by - empty for locally built images.</summary>
    public record ServiceImageDto(string ServiceName, string Image, string[] RepoDigests);
}
