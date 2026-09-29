using DockiUp.Domain;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Interfaces
{
    public interface IDockiUpDbContext
    {
        public DbSet<ProjectInfo> ProjectInfo { get; set; }
        public DbSet<Node> Nodes { get; set; }
        public DbSet<ActivityEntry> ActivityEntries { get; set; }
        public DbSet<Secret> Secrets { get; set; }
        public DbSet<Deployment> Deployments { get; set; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
