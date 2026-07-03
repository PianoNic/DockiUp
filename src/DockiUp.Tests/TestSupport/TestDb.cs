using System.Collections.Concurrent;
using DockiUp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DockiUp.Tests.TestSupport;

/// <summary>Builds an isolated in-memory <see cref="DockiUpDbContext"/> per test (unique database name),
/// so DB-backed handlers and services can be exercised without Postgres.</summary>
public static class TestDb
{
    // A shared root per database name makes the same name resolve to the SAME store even across different
    // service providers (a standalone assertion context vs. a scope factory's provider).
    private static readonly ConcurrentDictionary<string, InMemoryDatabaseRoot> Roots = new();

    private static InMemoryDatabaseRoot RootFor(string name) => Roots.GetOrAdd(name, _ => new InMemoryDatabaseRoot());

    /// <summary>A context over a brand-new, uniquely-named in-memory store.</summary>
    public static DockiUpDbContext Create() => Create(Guid.NewGuid().ToString());

    /// <summary>A context over the given named in-memory store — pass the same name to share data across
    /// contexts (e.g. a hub's scoped context and the test's assertion context).</summary>
    public static DockiUpDbContext Create(string databaseName)
    {
        var options = new DbContextOptionsBuilder<DockiUpDbContext>()
            .UseInMemoryDatabase(databaseName, RootFor(databaseName))
            .EnableSensitiveDataLogging()
            .Options;
        return new DockiUpDbContext(options);
    }

    /// <summary>A scope factory whose every scope yields a fresh <see cref="DockiUpDbContext"/> over the
    /// same named store — so code that resolves + disposes a scoped context (like NodeHub) doesn't dispose
    /// the test's own context.</summary>
    public static IServiceScopeFactory ScopeFactory(string databaseName)
    {
        var root = RootFor(databaseName);
        var services = new ServiceCollection();
        services.AddDbContext<DockiUpDbContext>(o => o.UseInMemoryDatabase(databaseName, root));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
