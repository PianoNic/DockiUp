using System.Text;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.TestSupport;
using LibGit2Sharp;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Infrastructure;

/// <summary>Committing in-browser edits back to git (#66) against a purely local bare "origin", through
/// DockerService's file API exactly as the Files tab uses it.</summary>
public class ProjectFileGitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dockiup-filegit-" + Guid.NewGuid().ToString("N"));
    private readonly Signature _sig = new("tester", "t@t.t", DateTimeOffset.UtcNow);
    private readonly DockiUpProjectConfigurationService _git = new();
    private readonly DockerService _docker;
    private static readonly ProjectFileCommit Edit = new("alice: update docker-compose.yml", "alice");

    public ProjectFileGitTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "projects"));
        _docker = new DockerService(new Mock<IDockiUpDockerClient>().Object, TestDb.Create(),
            Options.Create(new SystemPaths { ProjectsPath = Path.Combine(_root, "projects") }), _git);
    }

    /// <summary>A bare origin holding docker-compose.yml, a working repo pushing to it, and a DockiUp clone.</summary>
    private async Task<(string Bare, string Src, string Clone)> Setup()
    {
        var bare = Path.Combine(_root, "origin.git");
        Repository.Init(bare, isBare: true);
        var src = Path.Combine(_root, "src");
        Repository.Init(src);
        File.WriteAllText(Path.Combine(src, "docker-compose.yml"), "services: {}\n");
        File.WriteAllText(Path.Combine(src, ".gitignore"), ".env\n");
        using (var repo = new Repository(src))
        {
            Commands.Stage(repo, "*");
            repo.Commit("init", _sig, _sig);
            repo.Network.Remotes.Add("origin", bare);
            repo.Network.Push(repo.Network.Remotes["origin"], repo.Head.CanonicalName, new PushOptions());
        }
        var clone = Path.Combine(_root, "projects", "app");
        await _git.CloneRepositoryAsync(clone, bare);
        return (bare, src, clone);
    }

    private static (string Sha, string MessageShort, string Author) Tip(string repoPath)
    {
        using var repo = new Repository(repoPath);
        return (repo.Head.Tip.Sha, repo.Head.Tip.MessageShort, repo.Head.Tip.Author.Name);
    }

    private static string Read(string clone) => File.ReadAllText(Path.Combine(clone, "docker-compose.yml"));

    [Fact]
    public async Task SavingTrackedFile_CommitsAndPushes_WithMessageAndAuthor()
    {
        var (bare, _, clone) = await Setup();

        var result = await _docker.WriteProjectFileAsync(clone, "docker-compose.yml", Encoding.UTF8.GetBytes("services:\n  web: {image: nginx}\n"), Edit);

        Assert.True(result.Tracked);
        var pushed = Tip(bare);
        Assert.Equal(pushed.Sha, result.Commit);
        Assert.Equal("alice: update docker-compose.yml", pushed.MessageShort);
        Assert.Equal("alice", pushed.Author);
        Assert.Equal(pushed.Sha, Tip(clone).Sha);

        // The next deploy's sync keeps the edit (it is the remote tip now) and reports it as the deployed commit.
        var sync = await _git.SyncRepositoryAsync(clone, null, null, _ => Task.CompletedTask);
        Assert.Equal(pushed.Sha, sync.After);
        Assert.Contains("nginx", File.ReadAllText(Path.Combine(clone, "docker-compose.yml")));
    }

    [Fact]
    public async Task SavingUntrackedFile_WritesWithoutCommitting()
    {
        var (bare, _, clone) = await Setup();
        var before = Tip(bare).Sha;

        var result = await _docker.WriteProjectFileAsync(clone, ".env", Encoding.UTF8.GetBytes("A=1"), Edit);

        Assert.False(result.Tracked);
        Assert.Null(result.Commit);
        Assert.Equal(before, Tip(bare).Sha);
        Assert.Equal("A=1", File.ReadAllText(Path.Combine(clone, ".env")));
    }

    [Fact]
    public async Task SavingUnchangedTrackedFile_DoesNotCommit()
    {
        var (bare, _, clone) = await Setup();
        var before = Tip(bare).Sha;

        var result = await _docker.WriteProjectFileAsync(clone, "docker-compose.yml", Encoding.UTF8.GetBytes("services: {}\n"), Edit);

        Assert.Equal(before, result.Commit);
        Assert.Equal(before, Tip(bare).Sha);
    }

    [Fact]
    public async Task PushRejected_BranchMovedUpstream_UndoesCommit_AndRestoresFile()
    {
        var (bare, src, clone) = await Setup();
        File.WriteAllText(Path.Combine(src, "other.txt"), "x");
        using (var repo = new Repository(src))
        {
            Commands.Stage(repo, "*");
            repo.Commit("upstream", _sig, _sig);
            repo.Network.Push(repo.Network.Remotes["origin"], repo.Head.CanonicalName, new PushOptions());
        }
        var cloneTip = Tip(clone).Sha;
        var original = Read(clone);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _docker.WriteProjectFileAsync(clone, "docker-compose.yml", Encoding.UTF8.GetBytes("services: {changed: {}}\n"), Edit));

        Assert.True(ex.Message.Contains("rejected"), ex.Message);
        Assert.Equal(cloneTip, Tip(clone).Sha);
        Assert.Equal(original, Read(clone));
        using var cloneRepo = new Repository(clone);
        Assert.False(cloneRepo.RetrieveStatus().IsDirty);
    }

    [Fact]
    public async Task PushFails_UnreachableRemote_ExplainsCredentials_AndRestoresFile()
    {
        var (_, _, clone) = await Setup();
        using (var repo = new Repository(clone))
            repo.Network.Remotes.Update("origin", r => r.Url = Path.Combine(_root, "gone.git"));
        var original = Read(clone);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _docker.WriteProjectFileAsync(clone, "docker-compose.yml", Encoding.UTF8.GetBytes("services: {changed: {}}\n"), Edit));

        Assert.Contains("credentials", ex.Message);
        Assert.Equal(original, Read(clone));
    }

    [Fact]
    public async Task TrackedFiles_AreListed_AndCannotBeDeleted()
    {
        var (_, _, clone) = await Setup();
        File.WriteAllText(Path.Combine(clone, ".env"), "A=1");

        var entries = await _docker.ListProjectFilesAsync(clone, null);
        Assert.DoesNotContain(entries, e => e.Name == ".git");
        Assert.True(entries.Single(e => e.Name == "docker-compose.yml").Tracked);
        Assert.False(entries.Single(e => e.Name == ".env").Tracked);
        Assert.True((await _docker.ReadProjectFileAsync(clone, "docker-compose.yml")).Tracked);

        await Assert.ThrowsAsync<ArgumentException>(() => _docker.DeleteProjectFileAsync(clone, "docker-compose.yml"));
        await _docker.DeleteProjectFileAsync(clone, ".env");
        Assert.False(File.Exists(Path.Combine(clone, ".env")));
    }

    [Fact]
    public async Task ProjectFolderOutsideProjectsRoot_IsRefused()
    {
        var outside = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(outside);

        await Assert.ThrowsAsync<ArgumentException>(() => _docker.ListProjectFilesAsync(outside, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _docker.WriteProjectFileAsync(outside, "x", [1], null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _docker.ListProjectFilesAsync(Path.Combine(_root, "projects", "missing"), null));
    }

    [Fact]
    public async Task NonGitProject_FileApiWorks()
    {
        var project = Path.Combine(_root, "projects", "plain");
        Directory.CreateDirectory(project);

        var result = await _docker.WriteProjectFileAsync(project, "conf/a.conf", Encoding.UTF8.GetBytes("x"), null);
        Assert.False(result.Tracked);
        await _docker.CreateProjectFolderAsync(project, "certs");
        Assert.Equal(["certs", "conf"], (await _docker.ListProjectFilesAsync(project, null)).Select(e => e.Name));
        Assert.Equal("x", (await _docker.ReadProjectFileAsync(project, "conf/a.conf")).Content);
        Assert.Equal("x"u8.ToArray(), await _docker.DownloadProjectFileAsync(project, "conf/a.conf"));
        Assert.Empty(_git.GetTrackedFiles(project));
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
