using System.Text;
using DockiUp.API.Controllers;
using DockiUp.Application.Commands;
using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Domain;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Toamaisutaa.Abstractions;

namespace DockiUp.Tests.Api;

public class ProjectFilesControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Guid _projectId = Guid.NewGuid();

    public ProjectFilesControllerTests()
    {
        _mediator.Setup(m => m.Send(It.IsAny<SaveProjectFileCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ProjectFileWriteResult>(new ProjectFileWriteResult(true, "sha")));
    }

    private ProjectFilesController Controller(ICurrentUser? user = null) => new(_mediator.Object, user)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    [Fact]
    public async Task List_And_Read_ReturnOk()
    {
        var listing = new ProjectFilesDto("", false, "dockiup_compose.yml", []);
        _mediator.Setup(m => m.Send(It.Is<ListProjectFilesQuery>(q => q.ProjectId == _projectId && q.Path == "conf"), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ProjectFilesDto>(listing));
        var content = new ProjectFileContentDto("a", "b", 1, DateTime.UtcNow, false);
        _mediator.Setup(m => m.Send(It.IsAny<ReadProjectFileQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ProjectFileContentDto>(content));

        Assert.Same(listing, Assert.IsType<OkObjectResult>((await Controller().ListProjectFiles(_projectId, "conf")).Result).Value);
        Assert.Same(content, Assert.IsType<OkObjectResult>((await Controller().ReadProjectFile(_projectId, "a")).Result).Value);
    }

    [Fact]
    public async Task Save_WithoutDeploy_WritesUtf8_AndQueuesNothing()
    {
        var result = await Controller().SaveProjectFile(_projectId, new SaveProjectFileRequest(".env", "A=ä", false));

        var dto = Assert.IsType<SaveProjectFileResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("sha", dto.Commit);
        Assert.Null(dto.Deployment);
        _mediator.Verify(m => m.Send(It.Is<SaveProjectFileCommand>(c => c.Path == ".env" && Encoding.UTF8.GetString(c.Content) == "A=ä" && c.ActorName == null), It.IsAny<CancellationToken>()));
        _mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveAndDeploy_QueuesManualDeployment_AsTheUser()
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(u => u.IsAuthenticated).Returns(true);
        user.Setup(u => u.Name).Returns("alice");
        var deployment = new DeploymentDto(Guid.NewGuid(), _projectId, DeploymentTrigger.Manual, DeploymentStatus.Queued, "alice", null, null, null, null, DateTime.UtcNow, null, null, null, null);
        _mediator.Setup(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<DeploymentDto>(deployment));

        var result = await Controller(user.Object).SaveProjectFile(_projectId, new SaveProjectFileRequest("compose.yml", "services: {}", true));

        Assert.Same(deployment, Assert.IsType<SaveProjectFileResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value).Deployment);
        _mediator.Verify(m => m.Send(It.Is<SaveProjectFileCommand>(c => c.ActorName == "alice"), It.IsAny<CancellationToken>()));
        _mediator.Verify(m => m.Send(It.Is<QueueDeploymentCommand>(c => c.ProjectId == _projectId && c.Trigger == DeploymentTrigger.Manual && c.ActorName == "alice"), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Save_TooLarge_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Controller().SaveProjectFile(_projectId, new SaveProjectFileRequest("big", new string('a', 1024 * 1024 + 1), false)));
    }

    [Theory]
    [InlineData(null, "cert.pem", "cert.pem")]
    [InlineData("certs", "cert.pem", "certs/cert.pem")]
    [InlineData("certs/", "C:\\Users\\me\\cert.pem", "certs/cert.pem")]
    public async Task Upload_SavesIntoFolder_UnderItsFileName(string? folder, string clientName, string expected)
    {
        var file = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", clientName);

        var result = await Controller().UploadProjectFile(_projectId, file, folder);

        Assert.IsType<OkObjectResult>(result.Result);
        _mediator.Verify(m => m.Send(It.Is<SaveProjectFileCommand>(c => c.Path == expected && c.Content.Length == 3), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Download_ReturnsFileNamedAfterPath()
    {
        _mediator.Setup(m => m.Send(It.IsAny<DownloadProjectFileQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<byte[]>([7]));

        var result = Assert.IsType<FileContentResult>(await Controller().DownloadProjectFile(_projectId, "certs/cert.pem"));

        Assert.Equal("cert.pem", result.FileDownloadName);
        Assert.Equal([7], result.FileContents);
    }

    [Fact]
    public async Task Delete_And_CreateFolder_ReturnNoContent()
    {
        Assert.IsType<NoContentResult>(await Controller().DeleteProjectFile(_projectId, "x"));
        Assert.IsType<NoContentResult>(await Controller().CreateProjectFolder(_projectId, "y"));
        _mediator.Verify(m => m.Send(It.Is<DeleteProjectFileCommand>(c => c.Path == "x"), It.IsAny<CancellationToken>()));
        _mediator.Verify(m => m.Send(It.Is<CreateProjectFolderCommand>(c => c.Path == "y"), It.IsAny<CancellationToken>()));
    }
}
