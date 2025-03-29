using Microsoft.EntityFrameworkCore;

namespace DockiUp.Infrastructure
{
    public class DockiUpDbContext : DbContext
    {
        public required DbSet<DockiUp.Domain.Models.Container> Containers { get; set; }
        public required DbSet<DockiUp.Domain.Models.User> Users { get; set; }
        public required DbSet<DockiUp.Domain.Models.WebhookSecret> WebhookSecrets { get; set; }

        public DockiUpDbContext(DbContextOptions<DockiUpDbContext> options) : base(options) { }
    }
}
