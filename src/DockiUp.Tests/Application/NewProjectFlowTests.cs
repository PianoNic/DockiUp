using DockiUp.Application.Commands;
using DockiUp.Application.Compose;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DockiUp.Tests.Application;

public class ComposeValidationParseTests
{
    [Fact]
    public void Success_ListsServicesAndImages_KeepsWarnings()
    {
        var result = ComposeValidation.Parse(0,
            """{"name":"x","services":{"web":{"image":"nginx:1"},"worker":{"build":{"context":"."}}}}""",
            "time=\"2026-10-01T13:43:23+02:00\" level=warning msg=\"The \\\"OPT\\\" variable is not set. Defaulting to a blank string.\"\n");

        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
        Assert.Equal([new ComposeServiceDto("web", "nginx:1"), new ComposeServiceDto("worker", null)], result.Services);
        Assert.Equal(["The \"OPT\" variable is not set. Defaulting to a blank string."], result.Warnings);
    }

    [Fact]
    public void MissingRequiredVariable_IsAnError()
    {
        var result = ComposeValidation.Parse(1, "",
            "error while interpolating services.web.image: required variable TAG is missing a value: set TAG\n");

        Assert.False(result.Valid);
        Assert.Equal(["error while interpolating services.web.image: required variable TAG is missing a value: set TAG"], result.Errors);
        Assert.Empty(result.Services);
    }

    [Fact]
    public void FailureWithoutOutput_StillExplains()
        => Assert.Equal(["docker compose config failed (exit 15)."], ComposeValidation.Parse(15, "", "").Errors);

    [Fact]
    public void WarnBracketPrefix_IsStripped()
        => Assert.Equal(["the attribute `version` is obsolete"],
            ComposeValidation.Parse(0, """{"services":{"a":{}}}""", "WARN[0000] the attribute `version` is obsolete").Warnings);

    [Fact]
    public void NoServices_IsInvalid()
        => Assert.False(ComposeValidation.Parse(0, """{"services":{}}""", "").Valid);

    [Fact]
    public void UnreadableOutput_IsInvalid()
        => Assert.False(ComposeValidation.Parse(0, "not json", "").Valid);
}

public class ComposeFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dockiup-composefiles-" + Guid.NewGuid().ToString("N"));

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void FindsOnlyComposeYaml_RootFirst_SkipsDotFolders()
    {
        Write("docker-compose.yml", "services:\n  web:\n    image: nginx\n  db:\n    image: postgres\n");
        Write("deploy/prod.yaml", "services:\n  web:\n    image: nginx\n");
        Write("compose.yaml", "services: {}\n");
        Write(".github/workflows/ci.yml", "services:\n  pg:\n    image: postgres\n"); // CI, not deployable
        Write("k8s/deploy.yml", "apiVersion: apps/v1\nkind: Deployment\n");
        Write("broken.yml", "services: [unclosed\n");
        Write("README.md", "services:");

        var files = ComposeFiles.Find(_root);

        Assert.Equal(["compose.yaml", "docker-compose.yml", "deploy/prod.yaml"], files.Select(f => f.Path));
        Assert.Equal(["web", "db"], files[1].Services);
        Assert.Empty(files[0].Services);
        Assert.StartsWith("services:", files[1].Content);
    }

    [Fact]
    public void OversizedFiles_AreSkipped()
    {
        Write("docker-compose.yml", "services:\n  a:\n    image: x\n#" + new string('x', (int)ComposeFiles.MaxFileBytes));
        Assert.Empty(ComposeFiles.Find(_root));
    }

    [Fact]
    public void DefaultFile_PrefersStandardNames()
    {
        Assert.Equal("docker-compose.yml", ComposeFiles.DefaultFile(["deploy/a.yml", "docker-compose.yml"]));
        Assert.Equal("compose.yaml", ComposeFiles.DefaultFile(["docker-compose.yml", "compose.yaml"]));
        Assert.Equal("deploy/a.yml", ComposeFiles.DefaultFile(["deploy/a.yml"]));
        Assert.Null(ComposeFiles.DefaultFile([]));
    }

    [Theory]
    [InlineData("services:\n  a: {}\n", true)]
    [InlineData("- a\n- b\n", false)]
    [InlineData("services: 3\n", false)]
    [InlineData("", false)]
    public void ServicesOf_RecognisesCompose(string yaml, bool isCompose)
        => Assert.Equal(isCompose, ComposeFiles.ServicesOf(yaml) is not null);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }
}

public class AdoptProjectCommandTests
{
    private readonly DockiUp.Infrastructure.DockiUpDbContext _db = TestDb.Create();
    private readonly Mock<IDockerService> _docker = new();
    private readonly Mock<IDockerServiceResolver> _resolver = new();
    private readonly Mock<IActivityLogger> _activity = new();

    public AdoptProjectCommandTests()
    {
        _resolver.Setup(r => r.Resolve(It.IsAny<Guid?>())).Returns(_docker.Object);
    }

    private static ProjectDto Running(string name, string? workingDir = "/opt/stacks/blog", string? files = "/opt/stacks/blog/compose.yaml") => new()
    {
        ProjectName = name, DockerProjectName = name, ProjectDescription = "", ManagedByDockiUp = false, Containers = [],
        ComposeWorkingDir = workingDir, ComposeConfigFiles = files,
    };

    private AdoptProjectCommandHandler Handler() => new(_resolver.Object, _db, _activity.Object);

    private static AdoptProjectCommand Cmd(string name, Guid? node = null) => new(new AdoptProjectDto
    {
        DockerProjectName = name, NodeId = node, ProjectUpdateMethod = ProjectUpdateMethod.Manual, Description = "my blog",
    });

    [Fact]
    public async Task StoresTheProjectInPlace_FromItsComposeLabels_WithoutTouchingDocker()
    {
        var node = Guid.NewGuid();
        _docker.Setup(d => d.GetRawProjectsAsync()).ReturnsAsync([Running("blog", files: "/opt/stacks/blog/compose.yaml,/opt/stacks/blog/override.yaml")]);

        var result = await Handler().Handle(Cmd("Blog", node), CancellationToken.None);

        var saved = await _db.ProjectInfo.SingleAsync();
        Assert.Equal(saved.Id, result.Id);
        Assert.Equal("blog", result.DockerProjectName);
        Assert.Equal("blog", saved.DockerProjectName);
        Assert.Equal(ProjectOriginType.Adopted, saved.ProjectOrigin);
        Assert.Equal("/opt/stacks/blog", saved.ProjectPath);
        Assert.Equal("/opt/stacks/blog/compose.yaml,/opt/stacks/blog/override.yaml", saved.ComposePath);
        Assert.Equal(node, saved.NodeId);
        Assert.Equal("my blog", saved.Description);
        _resolver.Verify(r => r.Resolve(node));
        // Nothing restarted, pulled or deployed.
        _docker.Verify(d => d.ComposeUpAsync(It.IsAny<ComposeTarget>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        _docker.Verify(d => d.RestartProjectAsync(It.IsAny<string>()), Times.Never);
        _activity.Verify(a => a.LogAsync("adopt", "blog", saved.Id, "/opt/stacks/blog", It.IsAny<CancellationToken>(), null));
    }

    [Fact]
    public async Task UnknownProject_IsNotFound()
    {
        _docker.Setup(d => d.GetRawProjectsAsync()).ReturnsAsync([Running("other")]);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Handler().Handle(Cmd("blog"), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AlreadyManaged_IsRefused()
    {
        _docker.Setup(d => d.GetRawProjectsAsync()).ReturnsAsync([Running("blog")]);
        await Handler().Handle(Cmd("blog"), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => Handler().Handle(Cmd("blog"), CancellationToken.None).AsTask());
        Assert.Single(_db.ProjectInfo);
    }

    [Fact]
    public async Task WithoutComposeLabels_IsRefused()
    {
        _docker.Setup(d => d.GetRawProjectsAsync()).ReturnsAsync([Running("blog", workingDir: null, files: null)]);
        await Assert.ThrowsAsync<ArgumentException>(() => Handler().Handle(Cmd("blog"), CancellationToken.None).AsTask());
        Assert.Empty(_db.ProjectInfo);
    }

    [Fact]
    public void Validator_RequiresNameAndIntervalForPeriodic()
    {
        var validator = new AdoptProjectCommandValidator();
        Assert.True(validator.Validate(Cmd("blog")).IsValid);
        Assert.False(validator.Validate(Cmd("")).IsValid);
        Assert.False(validator.Validate(new AdoptProjectCommand(new AdoptProjectDto
        {
            DockerProjectName = "blog", ProjectUpdateMethod = ProjectUpdateMethod.Periodically,
        })).IsValid);
    }

    [Fact]
    public async Task RemovingAnAdoptedProject_LeavesItsFilesAlone()
    {
        var project = new ProjectInfo
        {
            ProjectName = "blog", DockerProjectName = "blog", ProjectOrigin = ProjectOriginType.Adopted,
            ProjectPath = "/opt/stacks/blog", ComposePath = "/opt/stacks/blog/compose.yaml", ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };
        _db.ProjectInfo.Add(project);
        await _db.SaveChangesAsync();

        await new RemoveProjectCommandHandler(_resolver.Object, _db, _activity.Object)
            .Handle(new RemoveProjectCommand(project.Id, null), CancellationToken.None);

        _docker.Verify(d => d.RemoveProjectAsync("blog", false));
        _docker.Verify(d => d.DeleteProjectFilesAsync(It.IsAny<string>()), Times.Never);
        Assert.Empty(_db.ProjectInfo);
    }
}

public class ValidateComposeQueryTests
{
    [Fact]
    public async Task RunsOnTheTargetNode()
    {
        var node = Guid.NewGuid();
        var docker = new Mock<IDockerService>();
        var expected = new ComposeValidationDto(true, [], [], [new ComposeServiceDto("web", "nginx")]);
        var request = new ComposeValidationRequest(node, "services: {}", null);
        docker.Setup(d => d.ValidateComposeAsync(request, null, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(node)).Returns(docker.Object);

        var result = await new ValidateComposeQueryHandler(resolver.Object).Handle(new ValidateComposeQuery(request), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ValidateCompose_ResolvesStoredCredentials_AndPassesThemSeparately()
    {
        var credentialId = Guid.NewGuid();
        var credentials = new DockiUp.Application.Git.GitCredentials("bot", "secret-token");
        var provider = new Mock<DockiUp.Application.Git.IGitCredentialsProvider>();
        provider.Setup(p => p.GetAsync(credentialId, It.IsAny<CancellationToken>())).ReturnsAsync(credentials);
        var docker = new Mock<IDockerService>();
        var request = new ComposeValidationRequest(null, null, null, "https://git.example/private.git", GitCredentialId: credentialId);
        docker.Setup(d => d.ValidateComposeAsync(request, credentials, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComposeValidationDto(true, [], [], []));
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(null)).Returns(docker.Object);

        var result = await new ValidateComposeQueryHandler(resolver.Object, provider.Object).Handle(new ValidateComposeQuery(request), CancellationToken.None);

        Assert.True(result.Valid);
        docker.Verify(d => d.ValidateComposeAsync(request, credentials, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Validator_NeedsContentOrRepo_AndARelativeComposeFile()
    {
        var validator = new ValidateComposeQueryValidator();
        Assert.False(validator.Validate(new ValidateComposeQuery(new ComposeValidationRequest(null, null, null))).IsValid);
        Assert.True(validator.Validate(new ValidateComposeQuery(new ComposeValidationRequest(null, "services: {}", "A=1"))).IsValid);
        Assert.True(validator.Validate(new ValidateComposeQuery(new ComposeValidationRequest(null, null, null, "https://x/r.git", null, "deploy/c.yml"))).IsValid);
        Assert.False(validator.Validate(new ValidateComposeQuery(new ComposeValidationRequest(null, null, null, "https://x/r.git", null, "../c.yml"))).IsValid);
    }
}

public class ComposeProjectArgsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockiup-args-" + Guid.NewGuid().ToString("N"));

    public ComposeProjectArgsTests() => Directory.CreateDirectory(_dir);

    private string Touch(string name, string content = "services: {}")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void NoEnvFile_NoEnvFileFlag()
    {
        var compose = Touch("c.yml");
        Assert.Equal(["-p", "app", "-f", compose], DockerService.ComposeProjectArgs(new ComposeTarget(_dir, compose, "app")));
    }

    [Fact]
    public void EnvFileNextToCompose_IsPassed()
    {
        var compose = Touch("c.yml");
        var env = Touch(".env", "A=1");
        Assert.Equal(["-p", "app", "-f", compose, "--env-file", env], DockerService.ComposeProjectArgs(new ComposeTarget(_dir, compose, "app")));
    }

    [Fact]
    public void SeveralComposeFiles_FromAdoptedLabels_EachGetAnF()
    {
        var a = Touch("a.yml");
        var b = Touch("b.yml");
        Assert.Equal(["-p", "app", "-f", a, "-f", b], DockerService.ComposeProjectArgs(new ComposeTarget(_dir, $"{a},{b}", "app")));
    }

    [Fact]
    public void UnreachableComposeFile_ExplainsWhy()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DockerService.ComposeProjectArgs(new ComposeTarget("/opt/stacks/blog", "/opt/stacks/blog/compose.yaml", "blog")));
        Assert.Contains("not reachable", ex.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
