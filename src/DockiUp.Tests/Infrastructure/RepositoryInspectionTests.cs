using Docker.DotNet;
using Docker.DotNet.Models;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.TestSupport;
using LibGit2Sharp;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Infrastructure;

/// <summary>Repo inspection against a purely local bare repo (no network): branches, default branch, and
/// which YAML files count as compose files.</summary>
public class RepositoryInspectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dockiup-inspect-test-" + Guid.NewGuid().ToString("N"));
    private readonly Signature _sig = new("tester", "t@t.t", DateTimeOffset.UtcNow);
    private readonly DockiUpProjectConfigurationService _svc = new();

    private string Dir(string name)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    private void CommitFiles(Repository repo, string workdir, Dictionary<string, string> files, string message)
    {
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(workdir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        Commands.Stage(repo, "*");
        repo.Commit(message, _sig, _sig);
    }

    /// <summary>origin.git with a default branch (main: compose at the root and in deploy/, plus a CI file)
    /// and a "next" branch that only has deploy/next.yml.</summary>
    private string Origin()
    {
        var bare = Dir("origin.git");
        Repository.Init(bare, isBare: true);
        var src = Dir("src");
        Repository.Init(src);
        using var repo = new Repository(src);
        repo.Refs.UpdateTarget("HEAD", "refs/heads/main");
        CommitFiles(repo, src, new()
        {
            ["docker-compose.yml"] = "services:\n  web:\n    image: nginx\n",
            ["deploy/prod.yml"] = "services:\n  web:\n    image: nginx\n  db:\n    image: postgres\n",
            [".gitlab-ci.yml"] = "stages: [build]\n",
        }, "main");
        repo.Network.Remotes.Add("origin", bare);
        repo.Network.Push(repo.Network.Remotes["origin"], "refs/heads/main", new PushOptions());

        var next = repo.CreateBranch("next");
        Commands.Checkout(repo, next);
        File.Delete(Path.Combine(src, "docker-compose.yml"));
        File.Delete(Path.Combine(src, "deploy", "prod.yml"));
        Commands.Stage(repo, "*");
        CommitFiles(repo, src, new() { ["deploy/next.yml"] = "services:\n  api:\n    image: api\n" }, "next");
        repo.Network.Push(repo.Network.Remotes["origin"], "refs/heads/next", new PushOptions());

        using (var origin = new Repository(bare))
            origin.Refs.UpdateTarget("HEAD", "refs/heads/main");
        return bare;
    }

    [Fact]
    public async Task DefaultBranch_ListsBranchesAndComposeFiles()
    {
        var bare = Origin();

        var result = await _svc.InspectRepositoryAsync(bare, null);

        Assert.Equal("main", result.Branch);
        Assert.Equal("main", result.DefaultBranch);
        Assert.Equal(["main", "next"], result.Branches);
        Assert.Equal(["docker-compose.yml", "deploy/prod.yml"], result.ComposeFiles.Select(f => f.Path));
        Assert.Equal(["web", "db"], result.ComposeFiles[1].Services);
        Assert.Equal("docker-compose.yml", result.DefaultComposeFile);
    }

    [Fact]
    public async Task OtherBranch_ShowsThatBranchesFiles()
    {
        var bare = Origin();

        var result = await _svc.InspectRepositoryAsync(bare, "next");

        Assert.Equal("next", result.Branch);
        Assert.Equal(["deploy/next.yml"], result.ComposeFiles.Select(f => f.Path));
        Assert.Equal("deploy/next.yml", result.DefaultComposeFile);
    }

    [Fact]
    public async Task UnreadableRepo_IsAClearArgumentException_AndLeavesNoTempFolder()
    {
        var before = Directory.GetDirectories(Path.GetTempPath(), "dockiup-inspect-*").Length;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _svc.InspectRepositoryAsync(Path.Combine(_root, "missing.git"), null));

        Assert.Contains("Could not read the repository", ex.Message);
        Assert.True(Directory.GetDirectories(Path.GetTempPath(), "dockiup-inspect-*").Length <= before);
    }

    [Fact]
    public async Task UnknownBranch_IsAClearArgumentException()
        => await Assert.ThrowsAsync<ArgumentException>(() => _svc.InspectRepositoryAsync(Origin(), "nope"));

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}

public class ComposeLabelsTests
{
    [Fact]
    public async Task RawProjects_CarryComposeWorkingDirAndFiles_ForAdoption()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dockiup-labels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var compose = Path.Combine(dir, "compose.yaml");
        File.WriteAllText(compose, "services: {}");
        try
        {
            var containers = new Mock<IContainerOperations>();
            containers.Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ContainerListResponse>
                {
                    Container("a", "here", dir, compose),
                    Container("b", "elsewhere", "/opt/stacks/x", "/opt/stacks/x/compose.yaml"),
                });
            var docker = new Mock<IDockerClient>();
            docker.Setup(d => d.Containers).Returns(containers.Object);
            var client = new Mock<IDockiUpDockerClient>();
            client.Setup(c => c.DockerClient).Returns(docker.Object);
            var svc = new DockerService(client.Object, TestDb.Create(), Options.Create(new SystemPaths { ProjectsPath = "/p" }), new Mock<IDockiUpProjectConfigurationService>().Object);

            var projects = (await svc.GetRawProjectsAsync()).ToDictionary(p => p.DockerProjectName);

            Assert.Equal(dir, projects["here"].ComposeWorkingDir);
            Assert.Equal(compose, projects["here"].ComposeConfigFiles);
            Assert.True(projects["here"].ComposeFilesReachable);
            Assert.Equal("/opt/stacks/x", projects["elsewhere"].ComposeWorkingDir);
            Assert.False(projects["elsewhere"].ComposeFilesReachable);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static ContainerListResponse Container(string id, string project, string workingDir, string files) => new()
    {
        ID = id,
        Names = new List<string> { "/" + project + "-web-1" },
        State = "running",
        Status = "Up",
        Labels = new Dictionary<string, string>
        {
            ["com.docker.compose.project"] = project,
            ["com.docker.compose.service"] = "web",
            ["com.docker.compose.project.working_dir"] = workingDir,
            ["com.docker.compose.project.config_files"] = files,
        },
    };
}
