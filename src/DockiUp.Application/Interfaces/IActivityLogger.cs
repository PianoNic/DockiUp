namespace DockiUp.Application.Interfaces
{
    /// <summary>Writes an audit record for an action. Background jobs pass no actor (rendered "system").</summary>
    public interface IActivityLogger
    {
        /// <param name="actorName">Overrides the request user, for queued work done on someone's behalf.</param>
        Task LogAsync(string action, string target, Guid? projectId = null, string? details = null, CancellationToken cancellationToken = default, string? actorName = null);
    }
}
