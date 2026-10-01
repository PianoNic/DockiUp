using DockiUp.API.Controllers;
using DockiUp.API.Nodes;
using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Application.Git;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Application.Notifications;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Infrastructure;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.Api;
using DockiUp.Tests.TestSupport;
using LibGit2Sharp;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Application;

public class EnvFileTests
{
    [Fact]
    public void Merge_MappedSecretsWinOverDotEnv_OthersKept_NewOnesAppended()
    {
        var existing = "# comment\nDB_PASSWORD=plain\nexport API_URL=https://x\nDB_PASSWORD=again\n";
        var merged = EnvFile.Merge(existing, new Dictionary<string, string> { ["DB_PASSWORD"] = "vault-pw", ["TOKEN"] = "t0k" });

        var lines = merged.Split('\n');
        Assert.Contains("# comment", lines);
        Assert.Contains("export API_URL=https://x", lines);
        Assert.Single(lines, l => l.StartsWith("DB_PASSWORD"));
        Assert.Contains("DB_PASSWORD='vault-pw'", lines);
        Assert.Contains("TOKEN='t0k'", lines);
        Assert.DoesNotContain("plain", merged);
    }

    [Fact]
    public void Merge_WithoutDotEnv_WritesOnlySecrets()
    {
        var merged = EnvFile.Merge(null, new Dictionary<string, string> { ["A"] = "1" });
        Assert.Contains("A='1'", merged);
    }

    [Fact]
    public void Merge_QuotesValuesSoComposeTakesThemLiterally()
    {
        var merged = EnvFile.Merge(null, new Dictionary<string, string>
        {
            ["DOLLAR"] = "p$ss${HOME}",
            ["QUOTE"] = "it's \"x\" \\ $y",
            ["MULTI"] = "a\nb",
        });

        Assert.Contains("DOLLAR='p$ss${HOME}'", merged); // single quotes: no interpolation
        Assert.Contains("QUOTE=\"it's \\\"x\\\" \\\\ \\$y\"", merged);
        Assert.Contains("MULTI=\"a\\nb\"", merged);
    }

    [Theory]
    [InlineData("DB_PASSWORD", true)]
    [InlineData("_x1", true)]
    [InlineData("1A", false)]
    [InlineData("A-B", false)]
    [InlineData("", false)]
    public void IsValidName(string name, bool valid) => Assert.Equal(valid, EnvFile.IsValidName(name));
}

public class WriteEnvFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agenttest-env-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly DockerService _docker;

    public WriteEnvFileTests()
    {
        _project = Path.Combine(_root, "app");
        Directory.CreateDirectory(_project);
        _docker = new DockerService(new Mock<IDockiUpDockerClient>().Object, TestDb.Create(),
            Options.Create(new SystemPaths { ProjectsPath = _root }), new Mock<IDockiUpProjectConfigurationService>().Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string Generated => Path.Combine(_project, EnvFile.GeneratedFileName);
    private string Compose => Path.Combine(_project, "docker-compose.yml");

    [Fact]
    public async Task Writes_MergedEnvFile_AndComposeGetsIt()
    {
        File.WriteAllText(Compose, "services: {}");
        File.WriteAllText(Path.Combine(_project, ".env"), "PORT=80\nDB_PASSWORD=old\n");

        await _docker.WriteEnvFileAsync(_project, Compose, new Dictionary<string, string> { ["DB_PASSWORD"] = "new" });

        var content = File.ReadAllText(Generated);
        Assert.Contains("PORT=80", content);
        Assert.Contains("DB_PASSWORD='new'", content);
        Assert.DoesNotContain("old", content);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Generated));
        Assert.Equal(["-p", "app", "-f", Compose, "--env-file", Generated],
            DockerService.ComposeProjectArgs(new ComposeTarget(_project, Compose, "app")));
    }

    [Fact]
    public async Task NoSecrets_RemovesGeneratedFile_SoComposeUsesPlainDotEnv()
    {
        File.WriteAllText(Compose, "services: {}");
        await _docker.WriteEnvFileAsync(_project, Compose, new Dictionary<string, string> { ["A"] = "1" });
        await _docker.WriteEnvFileAsync(_project, Compose, new Dictionary<string, string>());

        Assert.False(File.Exists(Generated));
        Assert.Equal(["-p", "app", "-f", Compose], DockerService.ComposeProjectArgs(new ComposeTarget(_project, Compose, "app")));

        // With no generated file, a plain .env next to the compose file is what compose gets.
        var dotEnv = Path.Combine(_project, ".env");
        File.WriteAllText(dotEnv, "PORT=80\n");
        Assert.Equal(["-p", "app", "-f", Compose, "--env-file", dotEnv], DockerService.ComposeProjectArgs(new ComposeTarget(_project, Compose, "app")));
    }

    [Fact]
    public async Task AdoptedProjectOutsideTheRoot_DeploysWithoutSecrets_ButRefusesToWriteThem()
    {
        var outside = Path.Combine(Path.GetTempPath(), "agenttest-adopted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var a = Path.Combine(outside, "compose.yml");
            var b = Path.Combine(outside, "compose.prod.yml");
            // Adopted projects record several compose files, comma-separated, outside the projects root.
            await _docker.WriteEnvFileAsync(outside, $"{a},{b}", new Dictionary<string, string>()); // no-op, no throw
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _docker.WriteEnvFileAsync(outside, $"{a},{b}", new Dictionary<string, string> { ["X"] = "1" }));
            Assert.Empty(Directory.EnumerateFiles(outside));
        }
        finally { Directory.Delete(outside, true); }
    }

    [Fact]
    public async Task GitCheckout_ExcludesGeneratedFileFromGit()
    {
        Repository.Init(_project);
        await _docker.WriteEnvFileAsync(_project, Compose, new Dictionary<string, string> { ["A"] = "1" });
        await _docker.WriteEnvFileAsync(_project, Compose, new Dictionary<string, string> { ["A"] = "2" });

        using var repo = new Repository(_project);
        Assert.True(repo.Ignore.IsPathIgnored(EnvFile.GeneratedFileName));
        Assert.Single(File.ReadAllLines(Path.Combine(_project, ".git", "info", "exclude")), l => l == "/" + EnvFile.GeneratedFileName);
    }

    [Fact]
    public async Task RefusesPathsOutsideTheProjectsFolder()
    {
        var outside = Path.Combine(Path.GetTempPath(), "agenttest-outside");
        await Assert.ThrowsAsync<ArgumentException>(() => _docker.WriteEnvFileAsync(outside, Path.Combine(outside, "c.yml"), new Dictionary<string, string> { ["A"] = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _docker.WriteEnvFileAsync(_project, Path.Combine(_root, "other", "c.yml"), new Dictionary<string, string> { ["A"] = "1" }));
    }
}

public class DeploymentSecretsAndCredentialsTests
{
    private readonly DockiUpDbContext _db = TestDb.Create();
    private readonly Mock<IDockerService> _docker = new();
    private readonly Mock<IDockerServiceResolver> _resolver = new();
    private readonly Mock<IDeploymentEvents> _events = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly List<string> _streamed = [];
    private readonly SecretsVaultService _vault;

    public DeploymentSecretsAndCredentialsTests()
    {
        _vault = TestVault.Create(_db);
        _resolver.Setup(r => r.Resolve(It.IsAny<Guid?>())).Returns(_docker.Object);
        _events.Setup(e => e.LogAsync(It.IsAny<Guid>(), It.IsAny<string>())).Callback((Guid _, string l) => _streamed.Add(l)).Returns(Task.CompletedTask);
        _events.Setup(e => e.ChangedAsync(It.IsAny<DeploymentDto>())).Returns(Task.CompletedTask);
    }

    private DeploymentRunner Runner() => new(_db, _resolver.Object, _events.Object, new Mock<IActivityLogger>().Object,
        _vault, new GitCredentialsProvider(_db, _vault), _publisher.Object);

    private async Task<(ProjectInfo Project, Deployment Deployment)> SeedAsync(ProjectOriginType origin = ProjectOriginType.Compose, Guid? credentialId = null)
    {
        var p = new ProjectInfo
        {
            ProjectName = "App", DockerProjectName = "app", ProjectOrigin = origin, Branch = "main", GitCredentialId = credentialId,
            ProjectPath = "/p/app", ComposePath = "/p/app/docker-compose.yml", ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };
        _db.ProjectInfo.Add(p);
        var d = new Deployment { ProjectId = p.Id, Trigger = DeploymentTrigger.Manual };
        _db.Deployments.Add(d);
        await _db.SaveChangesAsync();
        return (p, d);
    }

    private async Task MapAsync(Guid projectId, string env, string secretName, string value)
    {
        await _vault.StoreAsync(secretName, value);
        var secret = await _db.Secrets.SingleAsync(s => s.Name == secretName);
        _db.ProjectSecrets.Add(new ProjectSecret { ProjectId = projectId, EnvName = env, SecretId = secret.Id });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task MappedSecrets_AreWrittenBeforeComposeUp_NamesLogged_ValuesNever()
    {
        var (p, d) = await SeedAsync();
        await MapAsync(p.Id, "DB_PASSWORD", "db-pw", "hunter2-very-secret");
        IReadOnlyDictionary<string, string>? written = null;
        var order = new List<string>();
        _docker.Setup(x => x.WriteEnvFileAsync("/p/app", "/p/app/docker-compose.yml", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, IReadOnlyDictionary<string, string> s, CancellationToken _) => { written = s; order.Add("env"); })
            .Returns(Task.CompletedTask);
        // A compose step that echoes the value (e.g. a build arg) must still not leak it.
        _docker.Setup(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ComposeTarget _, Func<string, Task> log, CancellationToken _) =>
            {
                order.Add("up");
                await log("Step 3: ARG PASSWORD=hunter2-very-secret");
                return new ComposeUpResult(true);
            });

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id), CancellationToken.None);

        Assert.Equal(["env", "up"], order);
        Assert.Equal("hunter2-very-secret", written!["DB_PASSWORD"]);
        var saved = await _db.Deployments.SingleAsync();
        Assert.Equal(DeploymentStatus.Succeeded, saved.Status);
        Assert.Contains("DB_PASSWORD", saved.Log);
        Assert.DoesNotContain("hunter2-very-secret", saved.Log);
        Assert.DoesNotContain(_streamed, l => l.Contains("hunter2-very-secret"));
    }

    [Fact]
    public async Task FailureMessages_AreRedactedToo()
    {
        var (p, d) = await SeedAsync();
        await MapAsync(p.Id, "TOKEN", "tok", "s3cr3t-value");
        _docker.Setup(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("invalid value s3cr3t-value"));

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id), CancellationToken.None);

        var saved = await _db.Deployments.SingleAsync();
        Assert.Equal(DeploymentStatus.Failed, saved.Status);
        Assert.DoesNotContain("s3cr3t-value", saved.Error);
        Assert.DoesNotContain("s3cr3t-value", saved.Log);
    }

    [Fact]
    public async Task NoMappings_StillCallsWriteEnvFile_WithNothing_SoAStaleFileIsRemoved()
    {
        var (p, d) = await SeedAsync();
        _docker.Setup(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ComposeUpResult(false));

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id), CancellationToken.None);

        _docker.Verify(x => x.WriteEnvFileAsync("/p/app", "/p/app/docker-compose.yml",
            It.Is<IReadOnlyDictionary<string, string>>(s => s.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GitCredentials_ArePassedToTheSync_AndNotLogged()
    {
        var credential = new GitCredential { Name = "gh", Username = "bot", TokenEncrypted = ((ISecretsVaultService)_vault).Encrypt("ghp_tokentokentoken") };
        _db.GitCredentials.Add(credential);
        var (p, d) = await SeedAsync(ProjectOriginType.Git, credential.Id);
        GitCredentials? passed = null;
        _docker.Setup(x => x.SyncRepositoryAsync("/p/app", "main", null, It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>(), It.IsAny<GitCredentials?>()))
            .Callback((string _, string? _, string? _, Func<string, Task> _, CancellationToken _, GitCredentials? c) => passed = c)
            .ReturnsAsync(new GitSyncResult("a", "b", "main"));
        _docker.Setup(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ComposeUpResult(true));

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id), CancellationToken.None);

        Assert.Equal(new GitCredentials("bot", "ghp_tokentokentoken"), passed);
        Assert.DoesNotContain("ghp_tokentokentoken", (await _db.Deployments.SingleAsync()).Log);
    }

    [Fact]
    public async Task Finished_PublishesDeploymentFinished()
    {
        var (p, d) = await SeedAsync();
        _docker.Setup(x => x.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ComposeUpResult(true));

        await Runner().RunAsync(new DeploymentRequest(p.Id, DeploymentTrigger.Manual, d.Id), CancellationToken.None);

        _publisher.Verify(x => x.Publish(It.Is<DeploymentFinished>(e => e.Succeeded && e.ProjectName == "App" && e.DeploymentId == d.Id), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class GitCredentialPlumbingTests
{
    [Fact]
    public void ToString_NeverShowsTheToken()
        => Assert.DoesNotContain("ghp_secret", new GitCredentials("bot", "ghp_secret").ToString());

    [Fact]
    public void CredentialsProvider_IsNullForPublicRepos_AndUsesTokenAsPassword()
    {
        Assert.Null(DockiUpProjectConfigurationService.CredentialsProvider(null));

        var creds = Assert.IsType<UsernamePasswordCredentials>(
            DockiUpProjectConfigurationService.CredentialsProvider(new GitCredentials("", "tok"))!("https://x", null, SupportedCredentialTypes.UsernamePassword));
        Assert.Equal("git", creds.Username); // never empty: libgit2 rejects that
        Assert.Equal("tok", creds.Password);
    }

    [Fact]
    public async Task CloneAndSync_WithCredentials_WorkAgainstARepoThatNeedsNone()
    {
        var root = Path.Combine(Path.GetTempPath(), "agenttest-git-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "src");
            Repository.Init(source);
            using (var repo = new Repository(source))
            {
                File.WriteAllText(Path.Combine(source, "a.txt"), "1");
                Commands.Stage(repo, "a.txt");
                var sig = new Signature("t", "t@t", DateTimeOffset.UtcNow);
                repo.Commit("init", sig, sig);
            }
            var svc = new DockiUpProjectConfigurationService();
            var creds = new GitCredentials("bot", "tok");
            var clone = Path.Combine(root, "clone");
            var branch = await svc.CloneRepositoryAsync(clone, source, null, creds);
            var log = new List<string>();
            await svc.SyncRepositoryAsync(clone, branch, null, l => { log.Add(l); return Task.CompletedTask; }, creds);

            Assert.DoesNotContain(log, l => l.Contains("tok"));
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Provider_ReturnsNullForNoId_DecryptsStored_ThrowsForUnknown()
    {
        var db = TestDb.Create();
        var vault = TestVault.Create(db);
        var stored = new GitCredential { Name = "gh", Username = "u", TokenEncrypted = ((ISecretsVaultService)vault).Encrypt("pat") };
        db.GitCredentials.Add(stored);
        await db.SaveChangesAsync();
        var provider = new GitCredentialsProvider(db, vault);

        Assert.Null(await provider.GetAsync(null));
        Assert.Equal(new GitCredentials("u", "pat"), await provider.GetAsync(stored.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoteDockerService_SendsCredentialsAndSecretsInThePayload()
    {
        var nodeId = Guid.NewGuid();
        var registry = new Mock<INodeRegistry>();
        var connection = "c1";
        registry.Setup(r => r.TryGetConnectionId(nodeId, out connection)).Returns(true);
        var proxy = new Mock<ISingleClientProxy>();
        var calls = new List<(string Method, object?[] Args)>();
        proxy.Setup(p => p.InvokeCoreAsync<GitSyncResult>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback((string m, object?[] a, CancellationToken _) => calls.Add((m, a))).ReturnsAsync(new GitSyncResult("a", "b", "main"));
        proxy.Setup(p => p.InvokeCoreAsync<bool>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback((string m, object?[] a, CancellationToken _) => calls.Add((m, a))).ReturnsAsync(true);
        var hub = new Mock<IHubContext<NodeHub>>();
        hub.Setup(h => h.Clients.Client("c1")).Returns(proxy.Object);
        var remote = new RemoteDockerService(nodeId, hub.Object, registry.Object, new DeployLogRelay());
        var creds = new GitCredentials("bot", "tok");

        await remote.SyncRepositoryAsync("/p/app", "main", null, _ => Task.CompletedTask, default, creds);
        await remote.WriteEnvFileAsync("/p/app", "/p/app/c.yml", new Dictionary<string, string> { ["A"] = "v" });

        Assert.Equal("SyncRepository", calls[0].Method);
        Assert.Same(creds, calls[0].Args[4]);
        Assert.Equal("WriteEnvFile", calls[1].Method);
        Assert.Equal("v", ((Dictionary<string, string>)calls[1].Args[2]!)["A"]);
    }
}

public class ProjectSettingsAndCredentialControllersTests
{
    private readonly DockiUpDbContext _db = TestDb.Create();
    private readonly SecretsVaultService _vault;
    private readonly ProjectInfo _project;

    public ProjectSettingsAndCredentialControllersTests()
    {
        _vault = TestVault.Create(_db);
        _project = new ProjectInfo
        {
            ProjectName = "App", DockerProjectName = "app", ProjectOrigin = ProjectOriginType.Git,
            ProjectPath = "/p/app", ComposePath = "/p/app/c.yml", ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };
        _db.ProjectInfo.Add(_project);
        _db.SaveChanges();
    }

    private ProjectSettingsController Settings() => new(_db, new Mock<IActivityLogger>().Object);
    private GitCredentialsController Credentials() => new(_db, _vault);
    private static T Value<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;

    private async Task<Guid> SecretAsync(string name)
    {
        await _vault.StoreAsync(name, "value-" + name);
        return (await _db.Secrets.SingleAsync(s => s.Name == name)).Id;
    }

    [Fact]
    public async Task SetProjectSecrets_ReplacesMappings_AndNeverReturnsValues()
    {
        var a = await SecretAsync("a");
        var b = await SecretAsync("b");
        await Settings().SetProjectSecrets(_project.Id, [new("OLD", a)], default);

        var dto = Value(await Settings().SetProjectSecrets(_project.Id, [new(" DB_PASSWORD ", a), new("API_KEY", b)], default));

        Assert.Equal(["API_KEY", "DB_PASSWORD"], dto.Secrets.Select(s => s.EnvName));
        Assert.Equal("b", dto.Secrets[0].SecretName);
        Assert.Equal(2, await _db.ProjectSecrets.CountAsync());
        Assert.DoesNotContain("value-", System.Text.Json.JsonSerializer.Serialize(dto));
        Assert.True(Value(await Settings().GetProjectSettings(_project.Id, default)).IsGit);
    }

    [Fact]
    public async Task SetProjectSecrets_Validates()
    {
        var a = await SecretAsync("a");
        await Assert.ThrowsAsync<ArgumentException>(() => Settings().SetProjectSecrets(_project.Id, [new("1BAD", a)], default));
        await Assert.ThrowsAsync<ArgumentException>(() => Settings().SetProjectSecrets(_project.Id, [new("A", a), new("A", a)], default));
        await Assert.ThrowsAsync<ArgumentException>(() => Settings().SetProjectSecrets(_project.Id, [new("A", Guid.NewGuid())], default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Settings().SetProjectSecrets(Guid.NewGuid(), [], default));
    }

    [Fact]
    public async Task VaultSecret_InUse_CannotBeDeleted()
    {
        var a = await SecretAsync("a");
        await Settings().SetProjectSecrets(_project.Id, [new("A", a)], default);
        var controller = new SecretsController(_vault, new Mock<ISecretGeneratorService>().Object, _db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => controller.Delete("a", default));
        Assert.Contains("App", ex.Message);

        await Settings().SetProjectSecrets(_project.Id, [], default);
        Assert.IsType<NoContentResult>(await controller.Delete("a", default));
    }

    [Fact]
    public async Task GitCredentials_CreateMasked_UpdateKeepsToken_PickPerProject_DeleteRefusedWhileUsed()
    {
        var created = Value(await Credentials().CreateGitCredential(new("GitHub", "", "ghp_abcdefghijklmnop"), default));
        Assert.Equal("git", created.Username);
        Assert.Equal("••••••••mnop", created.TokenMasked);
        Assert.DoesNotContain("ghp_abcdefghijklmnop", (await _db.GitCredentials.SingleAsync()).TokenEncrypted);
        await Assert.ThrowsAsync<ArgumentException>(() => Credentials().CreateGitCredential(new("GitHub", "x", "y"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Credentials().CreateGitCredential(new("Other", "x", null), default));

        var updated = Value(await Credentials().UpdateGitCredential(created.Id, new("GitHub", "bot", null), default));
        Assert.Equal("••••••••mnop", updated.TokenMasked);

        var settings = Value(await Settings().SetProjectGitCredential(_project.Id, new(created.Id), default));
        Assert.Equal(created.Id, settings.GitCredentialId);
        Assert.Equal(1, Value(await Credentials().ListGitCredentials(default)).Single().ProjectCount);
        await Assert.ThrowsAsync<ArgumentException>(() => Credentials().DeleteGitCredential(created.Id, default));
        await Assert.ThrowsAsync<ArgumentException>(() => Settings().SetProjectGitCredential(_project.Id, new(Guid.NewGuid()), default));

        await Settings().SetProjectGitCredential(_project.Id, new(null), default);
        Assert.IsType<NoContentResult>(await Credentials().DeleteGitCredential(created.Id, default));
    }

    [Fact]
    public async Task SetProjectGitCredential_RefusedForComposeProjects()
    {
        _project.ProjectOrigin = ProjectOriginType.Compose;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Settings().SetProjectGitCredential(_project.Id, new(null), default));
    }
}
