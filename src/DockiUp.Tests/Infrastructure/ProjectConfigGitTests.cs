using DockiUp.Infrastructure.Services;
using LibGit2Sharp;

namespace DockiUp.Tests.Infrastructure;

/// <summary>Exercises clone + sync (fetch, hard reset to origin/branch) against a purely local bare repo
/// (no network), covering DockiUpProjectConfigurationService's repository operations.</summary>
public class ProjectConfigGitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dockiup-git-" + Guid.NewGuid().ToString("N"));
    private readonly Signature _sig = new("tester", "t@t.t", DateTimeOffset.UtcNow);
    private readonly DockiUpProjectConfigurationService _svc = new();
    private readonly List<string> _log = [];

    private Task Log(string line) { _log.Add(line); return Task.CompletedTask; }

    private string Dir(string name)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    private static void Commit(string workdir, string content, string message, Signature sig)
    {
        File.WriteAllText(Path.Combine(workdir, "VERSION.txt"), content);
        using var repo = new Repository(workdir);
        Commands.Stage(repo, "*");
        repo.Commit(message, sig, sig);
    }

    private static void Push(string src)
    {
        using var repo = new Repository(src);
        repo.Network.Push(repo.Network.Remotes["origin"], repo.Head.CanonicalName, new PushOptions());
    }

    /// <summary>A bare "origin" with one commit (v1), plus the working repo that pushes to it.</summary>
    private (string Bare, string Src) Origin()
    {
        var bare = Dir("origin.git");
        Repository.Init(bare, isBare: true);
        var src = Dir("src");
        Repository.Init(src);
        Commit(src, "v1", "v1", _sig);
        using (var repo = new Repository(src))
            repo.Network.Remotes.Add("origin", bare);
        Push(src);
        return (bare, src);
    }

    private static string Head(string path)
    {
        using var repo = new Repository(path);
        return repo.Head.Tip.Sha;
    }

    [Fact]
    public async Task Clone_ReturnsCheckedOutBranch()
    {
        var (bare, src) = Origin();
        var clone = Path.Combine(_root, "clone");

        var branch = await _svc.CloneRepositoryAsync(clone, bare);

        using var srcRepo = new Repository(src);
        Assert.Equal(srcRepo.Head.FriendlyName, branch);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(clone, "VERSION.txt")));
    }

    [Fact]
    public async Task Sync_WithPinnedCommit_RollsBackToThatVersion_ThenForwardAgain()
    {
        var (bare, src) = Origin();
        var clone = Path.Combine(_root, "clone");
        var branch = await _svc.CloneRepositoryAsync(clone, bare);
        var v1 = Head(clone);
        Commit(src, "v2", "v2 message", _sig);
        Push(src);
        var v2 = (await _svc.SyncRepositoryAsync(clone, branch, null, Log)).After;

        var back = await _svc.SyncRepositoryAsync(clone, branch, v1, Log);
        Assert.Equal(v1, back.After);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(clone, "VERSION.txt")));

        var forward = await _svc.SyncRepositoryAsync(clone, branch, null, Log);
        Assert.Equal(v2, forward.After);
        Assert.Equal("v2 message", forward.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SyncRepositoryAsync(clone, branch, new string('a', 40), Log));
    }

    [Fact]
    public async Task Sync_NewCommitUpstream_MovesToRemoteTip_AndReportsBeforeAfter()
    {
        var (bare, src) = Origin();
        var clone = Path.Combine(_root, "clone");
        var branch = await _svc.CloneRepositoryAsync(clone, bare);
        var before = Head(clone);

        Commit(src, "v2", "v2", _sig);
        Push(src);

        var result = await _svc.SyncRepositoryAsync(clone, branch, null, Log);

        Assert.Equal(before, result.Before);
        Assert.Equal(Head(src), result.After);
        Assert.NotEqual(result.Before, result.After);
        Assert.Equal(branch, result.Branch);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(clone, "VERSION.txt")));
        Assert.Contains(_log, l => l.StartsWith("Updated "));
    }

    [Fact]
    public async Task Sync_NothingNew_ReportsSameCommit()
    {
        var (bare, _) = Origin();
        var clone = Path.Combine(_root, "clone");
        await _svc.CloneRepositoryAsync(clone, bare);

        // Null branch = stay on the current one.
        var result = await _svc.SyncRepositoryAsync(clone, null, null, Log);

        Assert.Equal(result.Before, result.After);
        Assert.Contains(_log, l => l.Contains("no new commits"));
    }

    [Fact]
    public async Task Sync_DivergedLocalCommit_IsDiscarded_InFavourOfRemote_AndUntrackedFilesSurvive()
    {
        var (bare, src) = Origin();
        var clone = Path.Combine(_root, "clone");
        var branch = await _svc.CloneRepositoryAsync(clone, bare);

        // Local edit committed on the server + upstream moved on: a merge would conflict.
        Commit(clone, "local-hack", "local", _sig);
        Commit(src, "v2", "v2", _sig);
        Push(src);
        File.WriteAllText(Path.Combine(clone, "dockiup_compose.yml"), "services: {}");

        var result = await _svc.SyncRepositoryAsync(clone, branch, null, Log);

        Assert.Equal(Head(src), result.After);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(clone, "VERSION.txt")));
        Assert.True(File.Exists(Path.Combine(clone, "dockiup_compose.yml")), "UI-written compose file must survive the reset");
    }

    [Fact]
    public async Task Sync_UnknownBranch_Throws()
    {
        var (bare, _) = Origin();
        var clone = Path.Combine(_root, "clone");
        await _svc.CloneRepositoryAsync(clone, bare);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.SyncRepositoryAsync(clone, "does-not-exist", null, Log));
        Assert.Contains("does-not-exist", ex.Message);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                // Git marks some files read-only; clear before delete.
                foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_root, recursive: true);
            }
        }
        catch { }
    }
}
