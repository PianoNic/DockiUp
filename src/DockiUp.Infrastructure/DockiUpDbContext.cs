using DockiUp.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Infrastructure
{
    public class DockiUpDbContext : DbContext
    {
        public required DbSet<DockiUp.Domain.Models.Container> Containers { get; set; }
        public required DbSet<DockiUp.Domain.Models.User> Users { get; set; }
        public required DbSet<WebhookSecret> WebhookSecrets { get; set; }

        public DockiUpDbContext(DbContextOptions<DockiUpDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Container configuration
            modelBuilder.Entity<Container>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.ContainerId).IsRequired();
                entity.Property(c => c.Name).HasMaxLength(20).IsRequired();
                entity.Property(c => c.Description).HasMaxLength(100).IsRequired();
                entity.Property(c => c.GitUrl).HasMaxLength(100).IsRequired();
                entity.Property(c => c.Path).HasMaxLength(100).IsRequired();

                // Business rule: Container can have either WebhookSecret or CheckIntervals, not both
                entity.HasOne(c => c.WebhookSecret)
                    .WithMany()
                    .HasForeignKey("WebhookSecretId")
                    .IsRequired(false);

                // Add a check constraint for the business rule with MySQL compatible syntax
                entity.ToTable(tb => tb.HasCheckConstraint(
                    "CK_Container_UpdateMechanism",
                    "(`WebhookSecretId` IS NULL AND `CheckIntervals` IS NOT NULL) OR (`WebhookSecretId` IS NOT NULL AND `CheckIntervals` IS NULL) OR (`WebhookSecretId` IS NULL AND `CheckIntervals` IS NULL)"));
            });

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);
                entity.Property(u => u.Username).HasMaxLength(50).IsRequired();
                entity.Property(u => u.PasswordHash).HasMaxLength(100).IsRequired();
                entity.Property(u => u.Email).HasMaxLength(100).IsRequired();

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
}
