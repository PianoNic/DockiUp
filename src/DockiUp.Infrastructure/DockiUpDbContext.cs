using DockiUp.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DockiUp.Infrastructure
{
    public class DockiUpDbContext : DbContext
    {
        public DbSet<DockiUp.Domain.Models.Container> Containers { get; set; }
        public DbSet<DockiUp.Domain.Models.User> Users { get; set; }
        public DbSet<WebhookSecret> WebhookSecrets { get; set; }

        public DockiUpDbContext(DbContextOptions<DockiUpDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Container configuration
            modelBuilder.Entity<Container>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.ContainerId).IsRequired(false);
                entity.Property(c => c.Name).HasMaxLength(20).IsRequired();
                entity.Property(c => c.Description).HasMaxLength(100).IsRequired();
                entity.Property(c => c.GitUrl).HasMaxLength(100).IsRequired();
                entity.Property(c => c.Path).HasMaxLength(100).IsRequired();
                entity.Property(c => c.Status).HasConversion<string>();
                entity.Property(c => c.UpdateMethod).HasConversion<string>();

                // Configure the relationship with WebhookSecret
                entity.HasOne(c => c.WebhookSecret)
                      .WithOne()
                      .HasForeignKey<WebhookSecret>("ContainerId")
                      .IsRequired(false)
                      .OnDelete(DeleteBehavior.Cascade);

                // Add check constraints to enforce business rules using the correct column name
                entity.ToTable(t => t.HasCheckConstraint(
                    "CK_Container_WebhookOrCheckIntervals",
                    "(\"WebhookSecretId\" IS NULL OR \"CheckIntervals\" IS NULL) AND " +
                    "NOT (\"WebhookSecretId\" IS NOT NULL AND \"CheckIntervals\" IS NOT NULL)"));

                entity.ToTable(t => t.HasCheckConstraint(
                    "CK_Container_ManualNoWebhookOrIntervals",
                    "\"UpdateMethod\" != 'UpdateManually' OR " +
                    "(\"WebhookSecretId\" IS NULL AND \"CheckIntervals\" IS NULL)"));
            });

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);
                entity.Property(u => u.Username).HasMaxLength(50).IsRequired();
                entity.Property(u => u.PasswordHash).HasMaxLength(100).IsRequired();
                entity.Property(u => u.Email).HasMaxLength(100).IsRequired();
                entity.Property(u => u.UserRole).HasConversion<string>();
                // Configure UserSettings as owned entity
                entity.OwnsOne(u => u.UserSettings, settings =>
                {
                    settings.Property(s => s.PreferredColorScheme).HasConversion<string>();
                });
                // Add a unique constraint on username
                entity.HasIndex(u => u.Username).IsUnique();
                // Add a unique constraint on email
                entity.HasIndex(u => u.Email).IsUnique();
            });

            // WebhookSecret configuration
            modelBuilder.Entity<WebhookSecret>(entity =>
            {
                entity.HasKey(w => w.Id);
                entity.Property(w => w.Identifier).HasMaxLength(10).IsRequired();
                entity.Property(w => w.Secret).HasMaxLength(200).IsRequired();
                // Add a unique constraint on identifier
                entity.HasIndex(w => w.Identifier).IsUnique();
            });
        }
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
