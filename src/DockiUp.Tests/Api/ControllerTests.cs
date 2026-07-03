using DockiUp.API.Controllers;
using DockiUp.Application.Commands;
using DockiUp.Application.Dtos;
using DockiUp.Application.Enums;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Application.Queries;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Api;

/// <summary>Controller tests: every action delegates to <see cref="IMediator"/> (or the vault/generator
/// services for secrets) and maps the result to the correct <see cref="ActionResult"/>. HttpContext is
/// wired up because several actions read <c>HttpContext.RequestAborted</c>.</summary>
public class ControllerTests
{
    private static T WithHttp<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static ContainerDto Container(string id = "cid", UpdateMethodType state = UpdateMethodType.Running) => new()
    {
        Id = id,
        Name = "web",
        Status = "Up 2 minutes",
        State = state,
        ServiceName = "web",
        ProjectName = "proj",
    };

    private static ProjectDto Project(string name = "proj") => new()
    {
        ProjectName = name,
        DockerProjectName = name,
        ProjectDescription = "desc",
        ManagedByDockiUp = true,
        Containers = [],
    };

    private static ActivityEntryDto Activity() => new()
    {
        Id = Guid.NewGuid(),
        Action = "deploy",
        Target = "proj",
        CreatedAt = DateTime.UtcNow,
    };

    // ----------------------------------------------------------------- ActivityController

    [Fact]
    public async Task Activity_List_ReturnsOk_WithEntries_AndClampedLimitPassedThrough()
    {
        var mediator = new Mock<IMediator>();
        IReadOnlyList<ActivityEntryDto> entries = new List<ActivityEntryDto> { Activity(), Activity() };
        mediator.Setup(m => m.Send(It.IsAny<ListActivityQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<ActivityEntryDto>>(entries));

        var controller = new ActivityController(mediator.Object);

        var result = await controller.List(50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(entries, ok.Value);
        mediator.Verify(m => m.Send(It.Is<ListActivityQuery>(q => q.Limit == 50), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Activity_List_UsesDefaultLimit_WhenNotSupplied()
    {
        var mediator = new Mock<IMediator>();
        IReadOnlyList<ActivityEntryDto> entries = new List<ActivityEntryDto>();
        mediator.Setup(m => m.Send(It.IsAny<ListActivityQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<ActivityEntryDto>>(entries));

        var result = await new ActivityController(mediator.Object).List();

        Assert.IsType<OkObjectResult>(result);
        mediator.Verify(m => m.Send(It.Is<ListActivityQuery>(q => q.Limit == 200), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ----------------------------------------------------------------- AppController

    [Fact]
    public async Task App_GetAppInfo_ReturnsOk_WithAppInfo()
    {
        var mediator = new Mock<IMediator>();
        var info = new AppInfoDto { Version = "v1.2.3", Environment = "Testing" };
        mediator.Setup(m => m.Send(It.IsAny<GetAppInfoQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AppInfoDto>(info));

        var controller = WithHttp(new AppController(mediator.Object));

        var result = await controller.GetAppInfo();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(info, ok.Value);
        mediator.Verify(m => m.Send(It.IsAny<GetAppInfoQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ----------------------------------------------------------------- ContainerController

    [Fact]
    public async Task Container_GetContainer_ReturnsOk_WithContainer()
    {
        var mediator = new Mock<IMediator>();
        var dto = Container();
        var node = Guid.NewGuid();
        mediator.Setup(m => m.Send(It.IsAny<GetContainerQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ContainerDto>(dto));

        var controller = WithHttp(new ContainerController(mediator.Object));

        var result = await controller.GetContainer("cid", node);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(dto, ok.Value);
        mediator.Verify(m => m.Send(It.Is<GetContainerQuery>(q => q.ContainerId == "cid" && q.NodeId == node), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Container_GetContainer_ReturnsNotFound_WhenHandlerThrowsKeyNotFound()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetContainerQuery>(), It.IsAny<CancellationToken>()))
            .Throws(new KeyNotFoundException());

        var controller = WithHttp(new ContainerController(mediator.Object));

        var result = await controller.GetContainer("missing");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Container_StartContainer_ReturnsNoContent_AndSendsCommand()
    {
        var mediator = new Mock<IMediator>();
        var node = Guid.NewGuid();
        mediator.Setup(m => m.Send(It.IsAny<StartContainerCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));

        var controller = WithHttp(new ContainerController(mediator.Object));

        var result = await controller.StartContainer("cid", node);

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(It.Is<StartContainerCommand>(c => c.ContainerId == "cid" && c.NodeId == node), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Container_StopContainer_ReturnsNoContent_AndSendsCommand()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<StopContainerCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));

        var controller = WithHttp(new ContainerController(mediator.Object));

        var result = await controller.StopContainer("cid");

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(It.Is<StopContainerCommand>(c => c.ContainerId == "cid" && c.NodeId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Container_RestartContainer_ReturnsNoContent_AndSendsCommand()
    {
        var mediator = new Mock<IMediator>();
        var node = Guid.NewGuid();
        mediator.Setup(m => m.Send(It.IsAny<RestartContainerCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));

        var controller = WithHttp(new ContainerController(mediator.Object));

        var result = await controller.RestartContainer("cid", node);

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(It.Is<RestartContainerCommand>(c => c.ContainerId == "cid" && c.NodeId == node), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Container_GetContainerLogs_ReturnsOk_WithLogText()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetContainerLogsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<string>("log line 1\nlog line 2"));

        var controller = WithHttp(new ContainerController(mediator.Object));

        var result = await controller.GetContainerLogs("cid", 100);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("log line 1\nlog line 2", ok.Value);
        mediator.Verify(m => m.Send(It.Is<GetContainerLogsQuery>(q => q.ContainerId == "cid" && q.Tail == 100), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Container_GetContainerLogs_ReturnsNotFound_WhenHandlerThrowsKeyNotFound()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetContainerLogsQuery>(), It.IsAny<CancellationToken>()))
            .Throws(new KeyNotFoundException());

        var controller = WithHttp(new ContainerController(mediator.Object));

        var result = await controller.GetContainerLogs("missing");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ----------------------------------------------------------------- DashboardController

    [Fact]
    public async Task Dashboard_Stats_ReturnsOk_WithStats()
    {
        var mediator = new Mock<IMediator>();
        var stats = new DashboardStatsDto
        {
            TotalProjects = 3,
            TotalContainers = 7,
            RunningContainers = 5,
            RecentActivity = new List<ActivityEntryDto> { Activity() },
        };
        mediator.Setup(m => m.Send(It.IsAny<GetDashboardStatsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<DashboardStatsDto>(stats));

        var controller = new DashboardController(mediator.Object);

        var result = await controller.Stats(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(stats, ok.Value);
        mediator.Verify(m => m.Send(It.IsAny<GetDashboardStatsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ----------------------------------------------------------------- ProjectController

    [Fact]
    public async Task Project_DeployProject_ReturnsNoContent_AndSendsCommandWithDto()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<DeployProjectCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        var dto = new SetupProjectDto
        {
            ProjectName = "My App",
            ProjectOrigin = DockiUp.Domain.Enums.ProjectOriginType.Compose,
            Compose = "services: {}",
            ProjectUpdateMethod = DockiUp.Domain.Enums.ProjectUpdateMethod.Manual,
        };

        var controller = WithHttp(new ProjectController(mediator.Object));

        var result = await controller.DeployProject(dto);

        Assert.IsType<NoContentResult>(result.Result);
        mediator.Verify(m => m.Send(It.Is<DeployProjectCommand>(c => c.SetupContainerDto == dto), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Project_GetProjects_ReturnsOk_WithProjectsArray()
    {
        var mediator = new Mock<IMediator>();
        var projects = new[] { Project("a"), Project("b") };
        mediator.Setup(m => m.Send(It.IsAny<GetProjectsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ProjectDto[]>(projects));

        var controller = WithHttp(new ProjectController(mediator.Object));

        var result = await controller.GetContainers();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(projects, ok.Value);
        mediator.Verify(m => m.Send(It.IsAny<GetProjectsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Project_StopProject_ReturnsNoContent_AndSendsCommand()
    {
        var mediator = new Mock<IMediator>();
        var id = Guid.NewGuid();
        mediator.Setup(m => m.Send(It.IsAny<StopProjectCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));

        var controller = WithHttp(new ProjectController(mediator.Object));

        var result = await controller.StopProject(id, null);

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(It.Is<StopProjectCommand>(c => c.ProjectId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Project_RestartProject_ReturnsNoContent_AndSendsCommandByDockerName()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RestartProjectCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));

        var controller = WithHttp(new ProjectController(mediator.Object));

        var result = await controller.RestartProject(null, "myapp");

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(It.Is<RestartProjectCommand>(c => c.ProjectId == null && c.DockerProjectName == "myapp"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Project_GetProject_ReturnsOk_WhenFound()
    {
        var mediator = new Mock<IMediator>();
        var id = Guid.NewGuid();
        var project = Project("found");
        mediator.Setup(m => m.Send(It.IsAny<GetProjectQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ProjectDto?>(project));

        var controller = WithHttp(new ProjectController(mediator.Object));

        var result = await controller.GetProject(id, null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(project, ok.Value);
        mediator.Verify(m => m.Send(It.Is<GetProjectQuery>(q => q.ProjectId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Project_GetProject_ReturnsNotFound_WhenHandlerReturnsNull()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetProjectQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ProjectDto?>((ProjectDto?)null));

        var controller = WithHttp(new ProjectController(mediator.Object));

        var result = await controller.GetProject(null, "unknown");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Project_UpdateProject_ReturnsNoContent_AndSendsCommand()
    {
        var mediator = new Mock<IMediator>();
        var id = Guid.NewGuid();
        mediator.Setup(m => m.Send(It.IsAny<UpdateProjectCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));

        var controller = WithHttp(new ProjectController(mediator.Object));

        var result = await controller.UpdateProject(id);

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(It.Is<UpdateProjectCommand>(c => c.ProjectId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ----------------------------------------------------------------- SecretsController

    [Fact]
    public async Task Secrets_List_ReturnsOk_WithMetadata()
    {
        var vault = new Mock<ISecretsVaultService>();
        var generator = new Mock<ISecretGeneratorService>();
        IReadOnlyList<SecretDto> secrets = new List<SecretDto>
        {
            new() { Id = Guid.NewGuid(), Name = "db-password", CreatedAt = DateTime.UtcNow },
        };
        vault.Setup(v => v.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(secrets);

        var controller = new SecretsController(vault.Object, generator.Object);

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(secrets, ok.Value);
        vault.Verify(v => v.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Secrets_Store_ReturnsBadRequest_WhenNameMissing()
    {
        var vault = new Mock<ISecretsVaultService>();
        var generator = new Mock<ISecretGeneratorService>();

        var controller = new SecretsController(vault.Object, generator.Object);

        var result = await controller.Store(new SecretsController.StoreSecretRequest("   ", "value"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        vault.Verify(v => v.StoreAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        generator.Verify(g => g.Generate(), Times.Never);
    }

    [Fact]
    public async Task Secrets_Store_GeneratesValue_WhenNoValueSupplied_AndReturnsItOnce()
    {
        var vault = new Mock<ISecretsVaultService>();
        var generator = new Mock<ISecretGeneratorService>();
        generator.Setup(g => g.Generate()).Returns("generated-secret");
        vault.Setup(v => v.StoreAsync("api-key", "generated-secret", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controller = new SecretsController(vault.Object, generator.Object);

        var result = await controller.Store(new SecretsController.StoreSecretRequest("api-key", null), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SecretsController.GeneratedSecretDto>(ok.Value);
        Assert.Equal("api-key", dto.Name);
        Assert.Equal("generated-secret", dto.Value);
        generator.Verify(g => g.Generate(), Times.Once);
        vault.Verify(v => v.StoreAsync("api-key", "generated-secret", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Secrets_Store_StoresSuppliedValue_AndReturnsNoContent()
    {
        var vault = new Mock<ISecretsVaultService>();
        var generator = new Mock<ISecretGeneratorService>();
        vault.Setup(v => v.StoreAsync("api-key", "supplied", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controller = new SecretsController(vault.Object, generator.Object);

        var result = await controller.Store(new SecretsController.StoreSecretRequest("api-key", "supplied"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        vault.Verify(v => v.StoreAsync("api-key", "supplied", It.IsAny<CancellationToken>()), Times.Once);
        generator.Verify(g => g.Generate(), Times.Never);
    }

    [Fact]
    public async Task Secrets_Delete_ReturnsNoContent_WhenDeleted()
    {
        var vault = new Mock<ISecretsVaultService>();
        var generator = new Mock<ISecretGeneratorService>();
        vault.Setup(v => v.DeleteAsync("api-key", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var controller = new SecretsController(vault.Object, generator.Object);

        var result = await controller.Delete("api-key", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        vault.Verify(v => v.DeleteAsync("api-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Secrets_Delete_ReturnsNotFound_WhenMissing()
    {
        var vault = new Mock<ISecretsVaultService>();
        var generator = new Mock<ISecretGeneratorService>();
        vault.Setup(v => v.DeleteAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var controller = new SecretsController(vault.Object, generator.Object);

        var result = await controller.Delete("nope", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ----------------------------------------------------------------- WebhookController

    private static WebhookController Webhook(Mock<IMediator> mediator, string? configuredSecret)
        => WithHttp(new WebhookController(mediator.Object, Options.Create(new DockiUpWebhookOptions { WebhookSecret = configuredSecret })));

    [Fact]
    public async Task Webhook_Trigger_NoSecretConfigured_SendsUpdate_AndReturnsNoContent()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<UpdateProjectCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        var id = Guid.NewGuid();

        var result = await Webhook(mediator, null).Trigger(id, null, null);

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(It.Is<UpdateProjectCommand>(c => c.ProjectId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_Trigger_MatchingHeaderSecret_SendsUpdate_AndReturnsNoContent()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<UpdateProjectCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        var id = Guid.NewGuid();

        var result = await Webhook(mediator, "s3cr3t").Trigger(id, "s3cr3t", null);

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(It.Is<UpdateProjectCommand>(c => c.ProjectId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_Trigger_MatchingQuerySecret_WhenHeaderMissing_SendsUpdate()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<UpdateProjectCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        var id = Guid.NewGuid();

        var result = await Webhook(mediator, "s3cr3t").Trigger(id, null, "s3cr3t");

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(It.IsAny<UpdateProjectCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_Trigger_MissingSecret_ReturnsBadRequest_AndDoesNotSend()
    {
        var mediator = new Mock<IMediator>();

        var result = await Webhook(mediator, "s3cr3t").Trigger(Guid.NewGuid(), null, null);

        Assert.IsType<BadRequestObjectResult>(result);
        mediator.Verify(m => m.Send(It.IsAny<UpdateProjectCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Webhook_Trigger_WrongSecret_ReturnsBadRequest_AndDoesNotSend()
    {
        var mediator = new Mock<IMediator>();

        var result = await Webhook(mediator, "s3cr3t").Trigger(Guid.NewGuid(), "wrong", null);

        Assert.IsType<BadRequestObjectResult>(result);
        mediator.Verify(m => m.Send(It.IsAny<UpdateProjectCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
