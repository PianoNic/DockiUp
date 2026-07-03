using DockiUp.Infrastructure.Services;
using LibGit2Sharp;

namespace DockiUp.Tests.Infrastructure;

/// <summary>Exercises the git clone + fast-forward-pull logic against a purely local bare repo (no
/// network), covering DockiUpProjectConfigurationService's repository operations.</summary>
public class ProjectConfigGitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dockiup-git-" + Guid.NewGuid().ToString("N"));
    private readonly Signature _sig = new("tester", "t@t.t", DateTimeOffset.Now);

    private string Dir(string name)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    private void Commit(string workdir, string content, string message)
    {
        File.WriteAllText(Path.Combine(workdir, "VERSION.txt"), content);
        using var repo = new Repository(workdir);
        Commands.Stage(repo, "*");
        repo.Commit(message, _sig, _sig);
    }

    [Fact]
    public async Task Clone_ThenUpdate_FastForwardsToNewCommits()
    {
        var bare = Dir("origin.git");
        Repository.Init(bare, isBare: true);
        var src = Dir("src");
        Repository.Init(src);
        Commit(src, "v1", "v1");
        using (var repo = new Repository(src))
        {
            repo.Network.Remotes.Add("origin", bare);
            repo.Network.Push(repo.Network.Remotes["origin"], repo.Head.CanonicalName, new PushOptions());
        }

        var svc = new DockiUpProjectConfigurationService();
        var clone = Path.Combine(_root, "clone");

        // Clone brings v1 down.
        await svc.CloneRepositoryAsync(clone, bare);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(clone, "VERSION.txt")));

        // Advance origin to v2, then pull on the clone.
        Commit(src, "v2", "v2");
        using (var repo = new Repository(src))
            repo.Network.Push(repo.Network.Remotes["origin"], repo.Head.CanonicalName, new PushOptions());

        await svc.UpdateRepositoryAsync(clone);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(clone, "VERSION.txt")));
    }

    [Fact]
    public async Task Update_UpToDate_IsNoOp()
    {
        var bare = Dir("origin2.git");
        Repository.Init(bare, isBare: true);
        var src = Dir("src2");
        Repository.Init(src);
        Commit(src, "only", "c1");
        using (var repo = new Repository(src))
        {
            repo.Network.Remotes.Add("origin", bare);
            repo.Network.Push(repo.Network.Remotes["origin"], repo.Head.CanonicalName, new PushOptions());
        }

        var svc = new DockiUpProjectConfigurationService();
        var clone = Path.Combine(_root, "clone2");
        await svc.CloneRepositoryAsync(clone, bare);

        // No new commits upstream -> pull reports up-to-date, file unchanged, no throw.
        await svc.UpdateRepositoryAsync(clone);
        Assert.Equal("only", File.ReadAllText(Path.Combine(clone, "VERSION.txt")));
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
