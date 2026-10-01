using System.Text;
using DockiUp.Infrastructure.Services;

namespace DockiUp.Tests.Infrastructure;

/// <summary>Path confinement and the file operations of the project Files tab, on a temp folder.</summary>
public class ProjectFileSystemTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "dockiup-files-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private static readonly IReadOnlySet<string> NoneTracked = new HashSet<string>();

    public ProjectFileSystemTests()
    {
        _root = Path.Combine(_base, "proj");
        Directory.CreateDirectory(_root);
    }

    private string Put(string relative, string content)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("a/./b")]
    [InlineData("..\\outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("/")]
    [InlineData("\\windows\\win.ini")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("C:foo")]
    [InlineData(".git/config")]
    [InlineData("sub/.GIT/HEAD")]
    [InlineData(".dockiup.env")]
    public void Resolve_RejectsPathsLeavingTheFolderOrTouchingGit(string path)
    {
        Assert.Throws<ArgumentException>(() => ProjectFileSystem.Resolve(_root, path));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("compose.yml", "compose.yml")]
    [InlineData("conf/nginx.conf", "conf/nginx.conf")]
    [InlineData("conf\\nginx.conf", "conf/nginx.conf")]
    [InlineData("conf/", "conf")]
    public void Resolve_AcceptsRelativePathsInside(string? path, string expected)
    {
        var full = ProjectFileSystem.Resolve(_root, path);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, expected)).TrimEnd(Path.DirectorySeparatorChar), full);
    }

    [Fact]
    public void Resolve_RejectsSymlinkPointingOutside_AllowsOneInside()
    {
        File.WriteAllText(Path.Combine(_base, "secret.txt"), "s");
        Put("inside.txt", "i");
        try
        {
            File.CreateSymbolicLink(Path.Combine(_root, "escape"), Path.Combine(_base, "secret.txt"));
            Directory.CreateSymbolicLink(Path.Combine(_root, "escapedir"), _base);
            File.CreateSymbolicLink(Path.Combine(_root, "ok"), Path.Combine(_root, "inside.txt"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // creating symlinks needs privileges on Windows without developer mode
        }

        Assert.Throws<ArgumentException>(() => ProjectFileSystem.Resolve(_root, "escape"));
        Assert.Throws<ArgumentException>(() => ProjectFileSystem.Resolve(_root, "escapedir/secret.txt"));
        Assert.EndsWith("ok", ProjectFileSystem.Resolve(_root, "ok"));
    }

    [Fact]
    public void List_FoldersFirst_HidesGit_MarksTracked()
    {
        Put("b.txt", "bb");
        Put("A.env", "a");
        Put("conf/x.conf", "x");
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        Put(".dockiup.env", "SECRET=x"); // decrypted vault values: never listed

        var entries = ProjectFileSystem.List(_root, null, new HashSet<string> { "b.txt", "conf/x.conf" });

        Assert.Equal(["conf", "A.env", "b.txt"], entries.Select(e => e.Name));
        Assert.True(entries[0].IsDirectory);
        Assert.True(entries[0].Tracked); // holds a tracked file
        Assert.False(entries[1].Tracked);
        Assert.Equal(2, entries[2].Size);
        Assert.Equal(DateTimeKind.Utc, entries[2].ModifiedAt.Kind);

        var sub = ProjectFileSystem.List(_root, "conf", NoneTracked);
        Assert.Equal("conf/x.conf", Assert.Single(sub).Path);
        Assert.Throws<KeyNotFoundException>(() => ProjectFileSystem.List(_root, "missing", NoneTracked));
    }

    [Fact]
    public async Task Write_Read_Delete_RoundTrip()
    {
        var (full, previous) = await ProjectFileSystem.WriteAsync(_root, "conf/app.env", Encoding.UTF8.GetBytes("A=1\n"), default);
        Assert.Null(previous);
        Assert.True(File.Exists(full));

        var read = await ProjectFileSystem.ReadTextAsync(_root, "conf/app.env", new HashSet<string> { "conf/app.env" }, default);
        Assert.Equal("A=1\n", read.Content);
        Assert.Equal("conf/app.env", read.Path);
        Assert.True(read.Tracked);

        var (_, previous2) = await ProjectFileSystem.WriteAsync(_root, "conf/app.env", Encoding.UTF8.GetBytes("A=2\n"), default);
        Assert.Equal("A=1\n", Encoding.UTF8.GetString(previous2!));
        await ProjectFileSystem.RestoreAsync(full, previous2);
        Assert.Equal("A=1\n", File.ReadAllText(full));

        ProjectFileSystem.CreateFolder(_root, "empty/nested");
        Assert.True(Directory.Exists(Path.Combine(_root, "empty", "nested")));

        ProjectFileSystem.Delete(_root, "conf/app.env");
        Assert.False(File.Exists(full));
        ProjectFileSystem.Delete(_root, "empty");
        Assert.False(Directory.Exists(Path.Combine(_root, "empty")));
        Assert.Throws<KeyNotFoundException>(() => ProjectFileSystem.Delete(_root, "nope"));
        Assert.Throws<ArgumentException>(() => ProjectFileSystem.Delete(_root, ""));
        Assert.Throws<ArgumentException>(() => ProjectFileSystem.Delete(_root, ".git"));
    }

    [Fact]
    public async Task Write_RefusesFolderTargets_AndRestoreRemovesNewFile()
    {
        ProjectFileSystem.CreateFolder(_root, "dir");
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectFileSystem.WriteAsync(_root, "dir", [1], default));
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectFileSystem.WriteAsync(_root, "", [1], default));
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectFileSystem.WriteAsync(_root, "../x", [1], default));

        var (full, previous) = await ProjectFileSystem.WriteAsync(_root, "new.txt", [65], default);
        await ProjectFileSystem.RestoreAsync(full, previous);
        Assert.False(File.Exists(full));
    }

    [Fact]
    public async Task Read_RefusesBinary_TooLarge_AndMissing()
    {
        File.WriteAllBytes(Path.Combine(_root, "img.png"), [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]);
        File.WriteAllBytes(Path.Combine(_root, "latin1.txt"), [0x48, 0xE9, 0xFF]); // invalid UTF-8
        File.WriteAllBytes(Path.Combine(_root, "big.txt"), new byte[ProjectFileSystem.MaxTextBytes + 1].Select(_ => (byte)'a').ToArray());

        var binary = await Assert.ThrowsAsync<ArgumentException>(() => ProjectFileSystem.ReadTextAsync(_root, "img.png", NoneTracked, default));
        Assert.Contains("binary", binary.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectFileSystem.ReadTextAsync(_root, "latin1.txt", NoneTracked, default));
        var big = await Assert.ThrowsAsync<ArgumentException>(() => ProjectFileSystem.ReadTextAsync(_root, "big.txt", NoneTracked, default));
        Assert.Contains("too large", big.Message);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ProjectFileSystem.ReadTextAsync(_root, "missing.txt", NoneTracked, default));

        // Binary files still download.
        Assert.Equal(6, (await ProjectFileSystem.ReadBytesAsync(_root, "img.png", default)).Length);
    }

    [Fact]
    public async Task Read_StripsBom()
    {
        File.WriteAllBytes(Path.Combine(_root, "bom.yml"), [0xEF, 0xBB, 0xBF, (byte)'a']);
        Assert.Equal("a", (await ProjectFileSystem.ReadTextAsync(_root, "bom.yml", NoneTracked, default)).Content);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }
}
