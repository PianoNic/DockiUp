using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using Toamaisutaa.Abstractions;

namespace DockiUp.Infrastructure.Services
{
    public class ActivityLogger : IActivityLogger
    {
        private readonly IDockiUpDbContext _dbContext;
        private readonly ICurrentUser? _currentUser;

        // ICurrentUser is only registered when OIDC is on; without it every entry logs as system.
        public ActivityLogger(IDockiUpDbContext dbContext, ICurrentUser? currentUser = null)
        {
            _dbContext = dbContext;
            _currentUser = currentUser;
        }

        public async Task LogAsync(string action, string target, Guid? projectId = null, string? details = null, CancellationToken cancellationToken = default, string? actorName = null)
        {
            _dbContext.ActivityEntries.Add(new ActivityEntry
            {
                Action = action,
                Target = target,
                ProjectId = projectId,
                Details = details,
                // Background work has no request user, so it logs as system (null) too.
                ActorName = actorName ?? (_currentUser?.IsAuthenticated == true ? _currentUser.Name : null),
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
