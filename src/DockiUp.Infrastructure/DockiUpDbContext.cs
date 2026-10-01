using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DockiUp.Infrastructure
{
    public class DockiUpDbContext : DbContext, IDockiUpDbContext
    {
        public DbSet<ProjectInfo> ProjectInfo { get; set; }
        public DbSet<Node> Nodes { get; set; }
        public DbSet<ActivityEntry> ActivityEntries { get; set; }
        public DbSet<Secret> Secrets { get; set; }
        public DbSet<Deployment> Deployments { get; set; }
        public DbSet<NotificationChannel> NotificationChannels { get; set; }
        public DbSet<GitCredential> GitCredentials { get; set; }
        public DbSet<ProjectSecret> ProjectSecrets { get; set; }

        public DbSet<ImageUpdateStatus> ImageUpdates { get; set; }

        public DockiUpDbContext(DbContextOptions<DockiUpDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ProjectInfo>(e =>
            {
                e.Property(p => p.LastPeriodicUpdateAt);
            });

            modelBuilder.Entity<Deployment>(e =>
            {
                e.HasIndex(d => new { d.ProjectId, d.CreatedAt });
                e.Property(d => d.Status).HasConversion<string>();
                e.Property(d => d.Trigger).HasConversion<string>();
            });

            // Image updates (#68/#69): policy string-stored like the deployment enums; one status row per service.
            modelBuilder.Entity<ProjectInfo>(e =>
            {
                e.Property(p => p.ImageUpdatePolicy).HasConversion<string>();
            });
            modelBuilder.Entity<ImageUpdateStatus>(e =>
            {
                e.HasIndex(u => new { u.ProjectId, u.ServiceName }).IsUnique();
            });

            modelBuilder.Entity<Node>(e =>
            {
                e.HasKey(n => n.Id);
                e.HasIndex(n => n.TokenHash);
            });

            // Notifications, project secrets and git credentials (#75-#77).
            modelBuilder.Entity<NotificationChannel>(e => e.Property(c => c.Type).HasConversion<string>());
            modelBuilder.Entity<GitCredential>(e => e.HasIndex(c => c.Name).IsUnique());
            modelBuilder.Entity<ProjectSecret>(e =>
            {
                e.HasIndex(s => new { s.ProjectId, s.EnvName }).IsUnique();
                e.HasOne<ProjectInfo>().WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne<Secret>().WithMany().HasForeignKey(s => s.SecretId).OnDelete(DeleteBehavior.Cascade);
            });
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var entries = ChangeTracker.Entries<BaseEntity>();
            foreach (var entry in entries)
            {
                // CreatedAt/Id are init-set at construction (BaseEntity defaults); only UpdatedAt is bumped here.
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                }
            }
            return await base.SaveChangesAsync(cancellationToken);
        }

        public class DockiUpDbContextFactory : IDesignTimeDbContextFactory<DockiUpDbContext>
        {
            public DockiUpDbContext CreateDbContext(string[] args)
            {
                var optionsBuilder = new DbContextOptionsBuilder<DockiUpDbContext>();
                optionsBuilder.UseNpgsql();
                return new DockiUpDbContext(optionsBuilder.Options);
            }
        }
    }
}
