using System.Security.Cryptography;
using System.Text;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DockiUp.Tests.Infrastructure;

/// <summary>Covers the four DB-/filesystem-backed Infrastructure services:
/// <see cref="SecretGeneratorService"/>, <see cref="SecretsVaultService"/>,
/// <see cref="ActivityLogger"/> and <see cref="DockiUpProjectConfigurationService"/>.</summary>
public class InfraServicesTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    // -------- helpers --------

    private static string ValidMasterKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static IConfiguration Config(string? masterKey)
    {
        var dict = new Dictionary<string, string?>();
        if (masterKey is not null)
        {
            dict["Vault:MasterKey"] = masterKey;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static SecretsVaultService NewVault(DockiUp.Infrastructure.DockiUpDbContext db, string? masterKey = null)
        => new(db, Config(masterKey ?? ValidMasterKey()));

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dockiup_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    // ==================== SecretGeneratorService ====================

    [Fact]
    public void Generate_Returns32Chars()
    {
        var value = new SecretGeneratorService().Generate();

        Assert.NotNull(value);
        Assert.Equal(32, value.Length);
    }

    [Fact]
    public void Generate_UsesOnlyAllowedAlphabet()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var svc = new SecretGeneratorService();

        for (var i = 0; i < 20; i++)
        {
            var value = svc.Generate();
            Assert.All(value, c => Assert.Contains(c, alphabet));
        }
    }

    [Fact]
    public void Generate_ManyCalls_AreUnique()
    {
        var svc = new SecretGeneratorService();

        var generated = Enumerable.Range(0, 200).Select(_ => svc.Generate()).ToList();

        Assert.Equal(generated.Count, generated.Distinct().Count());
    }

    // ==================== SecretsVaultService ====================

    [Fact]
    public async Task Store_ThenRetrieve_RoundTripsPlaintext()
    {
        var db = TestDb.Create();
        var vault = NewVault(db);

        await vault.StoreAsync("db-password", "super-secret-value");
        var retrieved = await vault.RetrieveAsync("db-password");

        Assert.Equal("super-secret-value", retrieved);
    }

    [Fact]
    public async Task Store_PersistsCiphertext_NotPlaintext()
    {
        var db = TestDb.Create();
        var vault = NewVault(db);
        const string plaintext = "plain-text-secret";

        await vault.StoreAsync("api-key", plaintext);

        var row = await db.Secrets.SingleAsync(s => s.Name == "api-key");
        // AES-GCM: ciphertext length equals plaintext length but the bytes must differ.
        Assert.False(row.Ciphertext.SequenceEqual(Encoding.UTF8.GetBytes(plaintext)));
        Assert.NotEmpty(row.Nonce);
        Assert.NotEmpty(row.Tag);
    }

    [Fact]
    public async Task Retrieve_MissingKey_ReturnsNull()
    {
        var db = TestDb.Create();
        var vault = NewVault(db);

        var retrieved = await vault.RetrieveAsync("does-not-exist");

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task Store_ExistingName_OverwritesInPlace()
    {
        var db = TestDb.Create();
        var vault = NewVault(db);

        await vault.StoreAsync("token", "first-value");
        await vault.StoreAsync("token", "second-value");

        Assert.Equal("second-value", await vault.RetrieveAsync("token"));
        // Only one row, not two.
        Assert.Equal(1, await db.Secrets.CountAsync(s => s.Name == "token"));
    }

    [Fact]
    public async Task Delete_ExistingSecret_ReturnsTrueAndRemovesRow()
    {
        var db = TestDb.Create();
        var vault = NewVault(db);
        await vault.StoreAsync("to-delete", "value");

        var deleted = await vault.DeleteAsync("to-delete");

        Assert.True(deleted);
        Assert.Null(await vault.RetrieveAsync("to-delete"));
        Assert.False(await db.Secrets.AnyAsync(s => s.Name == "to-delete"));
    }

    [Fact]
    public async Task Delete_MissingSecret_ReturnsFalse()
    {
        var db = TestDb.Create();
        var vault = NewVault(db);

        var deleted = await vault.DeleteAsync("nope");

        Assert.False(deleted);
    }

    [Fact]
    public async Task List_ReturnsMetadataOrderedByName()
    {
        var db = TestDb.Create();
        var vault = NewVault(db);
        await vault.StoreAsync("zebra", "z");
        await vault.StoreAsync("apple", "a");
        await vault.StoreAsync("mango", "m");

        var list = await vault.ListAsync();

        Assert.Equal(new[] { "apple", "mango", "zebra" }, list.Select(s => s.Name).ToArray());
        // Metadata only; plaintext is never exposed.
        Assert.All(list, dto =>
        {
            Assert.NotEqual(Guid.Empty, dto.Id);
            Assert.NotEqual(default, dto.CreatedAt);
        });
    }

    [Fact]
    public async Task List_Empty_ReturnsEmptyList()
    {
        var db = TestDb.Create();
        var vault = NewVault(db);

        var list = await vault.ListAsync();

        Assert.Empty(list);
    }

    [Fact]
    public void Ctor_MissingMasterKey_Throws()
    {
        var db = TestDb.Create();

        var ex = Assert.Throws<InvalidOperationException>(() => new SecretsVaultService(db, Config(null)));
        Assert.Contains("Vault:MasterKey", ex.Message);
    }

    [Fact]
    public void Ctor_NonBase64MasterKey_Throws()
    {
        var db = TestDb.Create();

        Assert.Throws<InvalidOperationException>(() => new SecretsVaultService(db, Config("not valid base64 !!!")));
    }

    [Fact]
    public void Ctor_WrongLengthMasterKey_Throws()
    {
        var db = TestDb.Create();
        var shortKey = Convert.ToBase64String(new byte[16]); // 16 bytes, not 32

        var ex = Assert.Throws<InvalidOperationException>(() => new SecretsVaultService(db, Config(shortKey)));
        Assert.Contains("32 bytes", ex.Message);
    }

    [Fact]
    public async Task DistinctKeys_ProduceDifferentNonces()
    {
        var db = TestDb.Create();
        var vault = NewVault(db);

        await vault.StoreAsync("a", "same-plaintext");
        await vault.StoreAsync("b", "same-plaintext");

        var rowA = await db.Secrets.SingleAsync(s => s.Name == "a");
        var rowB = await db.Secrets.SingleAsync(s => s.Name == "b");
        // Random nonces mean identical plaintext yields different ciphertext.
        Assert.False(rowA.Nonce.SequenceEqual(rowB.Nonce));
        Assert.False(rowA.Ciphertext.SequenceEqual(rowB.Ciphertext));
    }

    // ==================== ActivityLogger ====================

    [Fact]
    public async Task LogAsync_PersistsEntry_WithGivenFields()
    {
        var db = TestDb.Create();
        var projectId = Guid.NewGuid();
        var logger = new ActivityLogger(db);

        await logger.LogAsync("Deploy", "web-app", projectId, "deployed v2");

        var entry = await db.ActivityEntries.SingleAsync();
        Assert.Equal("Deploy", entry.Action);
        Assert.Equal("web-app", entry.Target);
        Assert.Equal(projectId, entry.ProjectId);
        Assert.Equal("deployed v2", entry.Details);
        Assert.Null(entry.ActorName);
    }

    [Fact]
    public async Task LogAsync_DefaultsOptionalArgsToNull()
    {
        var db = TestDb.Create();
        var logger = new ActivityLogger(db);

        await logger.LogAsync("Start", "container-1");

        var entry = await db.ActivityEntries.SingleAsync();
        Assert.Equal("Start", entry.Action);
        Assert.Equal("container-1", entry.Target);
        Assert.Null(entry.ProjectId);
        Assert.Null(entry.Details);
        Assert.Null(entry.ActorName);
    }

    [Fact]
    public async Task LogAsync_RecordsSignedInUser_AndSystemWhenAnonymous()
    {
        var db = TestDb.Create();
        var user = new Moq.Mock<Toamaisutaa.Abstractions.ICurrentUser>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.Name).Returns("Ada");
        await new ActivityLogger(db, user.Object).LogAsync("Deploy", "web");

        user.SetupGet(u => u.IsAuthenticated).Returns(false);
        await new ActivityLogger(db, user.Object).LogAsync("Stop", "web");

        var byAction = await db.ActivityEntries.ToDictionaryAsync(e => e.Action, e => e.ActorName);
        Assert.Equal("Ada", byAction["Deploy"]);
        Assert.Null(byAction["Stop"]);
    }

    [Fact]
    public async Task LogAsync_MultipleCalls_PersistOneRowEach()
    {
        var db = TestDb.Create();
        var logger = new ActivityLogger(db);

        await logger.LogAsync("Start", "a");
        await logger.LogAsync("Stop", "b");
        await logger.LogAsync("Restart", "c");

        Assert.Equal(3, await db.ActivityEntries.CountAsync());
        Assert.Equal(
            new[] { "Restart", "Start", "Stop" },
            (await db.ActivityEntries.Select(e => e.Action).ToListAsync()).OrderBy(a => a).ToArray());
    }

    // ==================== DockiUpProjectConfigurationService ====================

    [Fact]
    public async Task WriteComposeFileAsync_WritesFileAndReturnsExpectedPath()
    {
        var dir = NewTempDir();
        var svc = new DockiUpProjectConfigurationService();
        const string content = "services:\n  web:\n    image: nginx:latest\n";

        var path = await svc.WriteComposeFileAsync(dir, content);

        Assert.Equal(Path.Combine(dir, "dockiup_compose.yml"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteComposeFileAsync_WritesExactContent()
    {
        var dir = NewTempDir();
        var svc = new DockiUpProjectConfigurationService();
        const string content = "version: '3'\nservices:\n  db:\n    image: postgres:16\n";

        var path = await svc.WriteComposeFileAsync(dir, content);

        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteComposeFileAsync_OverwritesExistingFile()
    {
        var dir = NewTempDir();
        var svc = new DockiUpProjectConfigurationService();

        await svc.WriteComposeFileAsync(dir, "first content");
        var path = await svc.WriteComposeFileAsync(dir, "second content");

        Assert.Equal("second content", await File.ReadAllTextAsync(path));
    }
}
