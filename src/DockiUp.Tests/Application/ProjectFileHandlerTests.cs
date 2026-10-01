using System.Text;
using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Tests.TestSupport;
using Moq;

namespace DockiUp.Tests.Application;

/// <summary>Project file handlers: routing to the project's host, compose validation before a save, the
/// commit-back request for git projects, and the compose file being undeletable.</summary>
public class ProjectFileHandlerTests
{
    private readonly DockiUp.Infrastructure.DockiUpDbContext _db = TestDb.Create();
    private readonly Mock<IDockerService> _docker = new();
    private readonly Mock<IDockerServiceResolver> _resolver = new();
    private readonly Mock<IActivityLogger> _activity = new();

    public ProjectFileHandlerTests()
    {
        _resolver.Setup(r => r.Resolve(It.IsAny<Guid?>())).Returns(_docker.Object);
        _docker.Setup(d => d.WriteProjectFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<ProjectFileCommit?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectFileWriteResult(true, "abcdef1234"));
    }

    private ProjectFileHandlers Handler() => new(_resolver.Object, _db, _activity.Object);

    private ProjectInfo Seed(ProjectOriginType origin, Guid? nodeId = null)
    {
        var p = new ProjectInfo
        {
            ProjectName = "Proj",
            DockerProjectName = "proj",
            ProjectOrigin = origin,
            ProjectPath = "/projects/proj",
            ComposePath = origin == ProjectOriginType.Git ? "/projects/proj/deploy/compose.yml" : "/projects/proj/dockiup_compose.yml",
            ProjectUpdateMethod = ProjectUpdateMethod.Manual,
            NodeId = nodeId,
        };
        _db.ProjectInfo.Add(p);
        _db.SaveChanges();
        return p;
    }

    [Fact]
    public async Task List_RoutesToNode_AndReportsGitAndComposeFile()
    {
        var node = Guid.NewGuid();
        var p = Seed(ProjectOriginType.Git, node);
        _docker.Setup(d => d.ListProjectFilesAsync("/projects/proj", "deploy", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ProjectFileEntryDto("compose.yml", "deploy/compose.yml", false, 1, DateTime.UtcNow, true)]);

        var result = await Handler().Handle(new ListProjectFilesQuery(p.Id, "deploy/"), CancellationToken.None);

        _resolver.Verify(r => r.Resolve(node));
        Assert.True(result.IsGit);
        Assert.Equal("deploy/compose.yml", result.ComposeFile);
        Assert.Equal("deploy", result.Path);
        Assert.Single(result.Entries);
    }

    [Fact]
    public async Task UnknownProject_Throws404()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await Handler().Handle(new ReadProjectFileQuery(Guid.NewGuid(), "x"), CancellationToken.None));
    }

    [Fact]
    public async Task SaveCompose_Invalid_IsRefused_BeforeWriting()
    {
        var p = Seed(ProjectOriginType.Compose);
        _docker.Setup(d => d.ValidateProjectComposeAsync(It.IsAny<ComposeTarget>(), "bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComposeValidationResult(false, ["services.web must be a mapping"], []));

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Handler().Handle(new SaveProjectFileCommand(p.Id, "dockiup_compose.yml", Encoding.UTF8.GetBytes("bad")), CancellationToken.None));

        Assert.Contains("services.web must be a mapping", ex.Message);
        _docker.Verify(d => d.WriteProjectFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<ProjectFileCommit?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveCompose_Valid_Writes_WithoutCommit_ForComposeProjects()
    {
        var p = Seed(ProjectOriginType.Compose);
        _docker.Setup(d => d.ValidateProjectComposeAsync(It.Is<ComposeTarget>(t => t.ComposePath == p.ComposePath && t.DockerProjectName == "proj"), "services: {}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComposeValidationResult(true, [], ["web"]));

        await Handler().Handle(new SaveProjectFileCommand(p.Id, "dockiup_compose.yml", Encoding.UTF8.GetBytes("services: {}")), CancellationToken.None);

        _docker.Verify(d => d.WriteProjectFileAsync("/projects/proj", "dockiup_compose.yml", It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()));
        _activity.Verify(a => a.LogAsync("file.save", "Proj", p.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>(), null));
    }

    [Fact]
    public async Task SaveOtherFile_InGitProject_AsksForCommit_NamedAfterActor_AndSkipsValidation()
    {
        var p = Seed(ProjectOriginType.Git);

        var result = await Handler().Handle(new SaveProjectFileCommand(p.Id, "config\\app.conf", [1], "alice"), CancellationToken.None);

        Assert.Equal("abcdef1234", result.Commit);
        _docker.Verify(d => d.ValidateProjectComposeAsync(It.IsAny<ComposeTarget>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _docker.Verify(d => d.WriteProjectFileAsync("/projects/proj", "config/app.conf", It.IsAny<byte[]>(),
            new ProjectFileCommit("alice: update config/app.conf", "alice"), It.IsAny<CancellationToken>()));
        _activity.Verify(a => a.LogAsync("file.save", "Proj", p.Id, "config/app.conf @ abcdef1", It.IsAny<CancellationToken>(), "alice"));
    }

    [Fact]
    public async Task Save_WithoutUser_CommitsAsDockiUp()
    {
        var p = Seed(ProjectOriginType.Git);

        await Handler().Handle(new SaveProjectFileCommand(p.Id, ".env", [1]), CancellationToken.None);

        _docker.Verify(d => d.WriteProjectFileAsync(It.IsAny<string>(), ".env", It.IsAny<byte[]>(),
            new ProjectFileCommit("DockiUp: update .env", "DockiUp"), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Delete_ComposeFile_IsRefused_OtherFilesDelegate()
    {
        var p = Seed(ProjectOriginType.Git);

        await Assert.ThrowsAsync<ArgumentException>(async () => await Handler().Handle(new DeleteProjectFileCommand(p.Id, "deploy/compose.yml"), CancellationToken.None));
        await Handler().Handle(new DeleteProjectFileCommand(p.Id, "certs"), CancellationToken.None);
        await Handler().Handle(new CreateProjectFolderCommand(p.Id, "new"), CancellationToken.None);

        _docker.Verify(d => d.DeleteProjectFileAsync("/projects/proj", "certs", It.IsAny<CancellationToken>()));
        _docker.Verify(d => d.CreateProjectFolderAsync("/projects/proj", "new", It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ReadAndDownload_Delegate()
    {
        var p = Seed(ProjectOriginType.Compose);
        var content = new ProjectFileContentDto(".env", "A=1", 3, DateTime.UtcNow, false);
        _docker.Setup(d => d.ReadProjectFileAsync("/projects/proj", ".env", It.IsAny<CancellationToken>())).ReturnsAsync(content);
        _docker.Setup(d => d.DownloadProjectFileAsync("/projects/proj", ".env", It.IsAny<CancellationToken>())).ReturnsAsync([65]);

        Assert.Same(content, await Handler().Handle(new ReadProjectFileQuery(p.Id, ".env"), CancellationToken.None));
        Assert.Equal([65], await Handler().Handle(new DownloadProjectFileQuery(p.Id, ".env"), CancellationToken.None));
    }

    [Theory]
    [InlineData("/p", "/p/compose.yml", "compose.yml")]
    [InlineData("/p/", "/p/a/compose.yml", "a/compose.yml")]
    [InlineData("C:\\p", "C:\\p\\compose.yml", "compose.yml")]
    [InlineData("/p", "/other/compose.yml", null)]
    public void ComposeFile_IsRelativeToProjectFolder(string projectPath, string composePath, string? expected)
    {
        var p = new ProjectInfo
        {
            ProjectName = "x", DockerProjectName = "x", ProjectOrigin = ProjectOriginType.Compose,
            ProjectPath = projectPath, ComposePath = composePath, ProjectUpdateMethod = ProjectUpdateMethod.Manual,
        };
        Assert.Equal(expected, ProjectFileHandlers.ComposeFile(p));
    }
}
