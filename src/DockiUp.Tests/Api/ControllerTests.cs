using System.Security.Cryptography;
using System.Text;
using DockiUp.API.Controllers;
using DockiUp.Application.Deployments;
using DockiUp.Domain;
using DockiUp.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
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
using Toamaisutaa.Abstractions;
using Toamaisutaa.AspNetCore;

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

    [Fact]
    public async Task App_GetAppInfo_WithoutAuthProvider_StaysOpen()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetAppInfoQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AppInfoDto>(new AppInfoDto { Version = "v1", Environment = "Testing" }));

        var result = await WithHttp(new AppController(mediator.Object)).GetAppInfo();

        var dto = Assert.IsType<AppInfoDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(dto.AuthEnabled);
        Assert.Equal(string.Empty, dto.Authority);
    }

    [Fact]
    public async Task App_GetAppInfo_WithAuthProvider_MapsToamaisutaaClientConfiguration()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetAppInfoQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AppInfoDto>(new AppInfoDto { Version = "v1", Environment = "Testing" }));
        var provider = new Mock<IToamaisutaaClientConfigurationProvider>();
        provider.Setup(p => p.GetConfiguration(It.IsAny<HttpContext>())).Returns(new ToamaisutaaClientConfiguration
        {
            Authority = "https://idp.example.com",
            ClientId = "dockiup",
            RedirectUri = "https://dockiup.example.com/",
            PostLogoutRedirectUri = "https://dockiup.example.com/bye",
            Scope = "openid profile email roles",
        });

        var result = await WithHttp(new AppController(mediator.Object, provider.Object)).GetAppInfo();

        var dto = Assert.IsType<AppInfoDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(dto.AuthEnabled);
        Assert.Equal("https://idp.example.com", dto.Authority);
        Assert.Equal("dockiup", dto.ClientId);
        Assert.Equal("https://dockiup.example.com/", dto.RedirectUri);
        Assert.Equal("https://dockiup.example.com/bye", dto.PostLogoutRedirectUri);
        Assert.Equal("openid profile email roles", dto.Scope);
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

    private static ProjectController ProjectCtl(Mock<IMediator> mediator, DockiUp.Infrastructure.DockiUpDbContext? db = null)
        => WithHttp(new ProjectController(mediator.Object, db ?? TestDb.Create(), new ConfigurationBuilder().Build()));

    private static DeploymentDto QueuedDto(Guid projectId, DeploymentTrigger trigger) => new(
        Guid.NewGuid(), projectId, trigger, DeploymentStatus.Queued, null, null, null, null, null, DateTime.UtcNow, null, null, null);

    [Fact]
    public async Task Project_DeployProject_ReturnsOk_WithQueuedDeployment()
    {
        var mediator = new Mock<IMediator>();
        var queued = QueuedDto(Guid.NewGuid(), DeploymentTrigger.Create);
        mediator.Setup(m => m.Send(It.IsAny<DeployProjectCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<DeploymentDto?>(queued));
        var dto = new SetupProjectDto
        {
            ProjectName = "My App",
            ProjectOrigin = DockiUp.Domain.Enums.ProjectOriginType.Compose,
            Compose = "services: {}",
            ProjectUpdateMethod = DockiUp.Domain.Enums.ProjectUpdateMethod.Manual,
        };

        var result = await ProjectCtl(mediator).DeployProject(dto);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(queued, ok.Value);
        mediator.Verify(m => m.Send(It.Is<DeployProjectCommand>(c => c.SetupContainerDto == dto), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Project_GetProjects_ReturnsOk_WithProjectsArray()
    {
        var mediator = new Mock<IMediator>();
        var projects = new[] { Project("a"), Project("b") };
        mediator.Setup(m => m.Send(It.IsAny<GetProjectsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ProjectDto[]>(projects));

        var result = await ProjectCtl(mediator).GetContainers();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(projects, ok.Value);
    }

    [Theory]
    [InlineData(ProjectAction.Start)]
    [InlineData(ProjectAction.Stop)]
    [InlineData(ProjectAction.Restart)]
    public async Task Project_Lifecycle_ReturnsNoContent_AndSendsLifecycleCommand(ProjectAction action)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ProjectLifecycleCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        var node = Guid.NewGuid();
        var controller = ProjectCtl(mediator);

        var result = action switch
        {
            ProjectAction.Start => await controller.StartProject(null, "myapp", node),
            ProjectAction.Stop => await controller.StopProject(null, "myapp", node),
            _ => await controller.RestartProject(null, "myapp", node),
        };

        Assert.IsType<NoContentResult>(result);
        mediator.Verify(m => m.Send(
            It.Is<ProjectLifecycleCommand>(c => c.Action == action && c.ProjectId == null && c.DockerProjectName == "myapp" && c.NodeId == node),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Project_GetProject_ReturnsOk_WhenFound()
    {
        var mediator = new Mock<IMediator>();
        var id = Guid.NewGuid();
        var project = Project("found");
        mediator.Setup(m => m.Send(It.IsAny<GetProjectQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ProjectDto?>(project));

        var result = await ProjectCtl(mediator).GetProject(id, null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(project, ok.Value);
    }

    [Fact]
    public async Task Project_GetProject_ReturnsNotFound_WhenHandlerReturnsNull()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetProjectQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ProjectDto?>((ProjectDto?)null));

        var result = await ProjectCtl(mediator).GetProject(null, "unknown");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Project_QueueDeployment_ReturnsAccepted_WithManualTrigger()
    {
        var mediator = new Mock<IMediator>();
        var id = Guid.NewGuid();
        var queued = QueuedDto(id, DeploymentTrigger.Manual);
        mediator.Setup(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<DeploymentDto>(queued));

        var result = await ProjectCtl(mediator).QueueDeployment(id);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        Assert.Same(queued, accepted.Value);
        mediator.Verify(m => m.Send(It.Is<QueueDeploymentCommand>(c => c.ProjectId == id && c.Trigger == DeploymentTrigger.Manual),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Project_GetWebhook_ReturnsUrlAndSecret_ForKnownProject_AndNotFoundOtherwise()
    {
        var db = TestDb.Create();
        var project = SeedProject(db, branch: "main");
        var controller = ProjectCtl(new Mock<IMediator>(), db);
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("dockiup.example.com");

        var original = Environment.GetEnvironmentVariable("PUBLIC_URL");
        Environment.SetEnvironmentVariable("PUBLIC_URL", null);
        try
        {
            var ok = Assert.IsType<OkObjectResult>((await controller.GetWebhook(project.Id)).Result);
            var info = Assert.IsType<WebhookInfoDto>(ok.Value);
            Assert.Equal($"https://dockiup.example.com/api/Webhook/{project.Id}", info.Url);
            Assert.Equal(project.WebhookSecret, info.Secret);
            Assert.Equal("main", info.Branch);

            Assert.IsType<NotFoundResult>((await controller.GetWebhook(Guid.NewGuid())).Result);
        }
        finally { Environment.SetEnvironmentVariable("PUBLIC_URL", original); }
    }

    private static DockiUp.Domain.ProjectInfo SeedProject(DockiUp.Infrastructure.DockiUpDbContext db, string? branch = null, string secret = "topsecret")
    {
        var project = new DockiUp.Domain.ProjectInfo
        {
            ProjectName = "app",
            DockerProjectName = "app",
            ProjectOrigin = DockiUp.Domain.Enums.ProjectOriginType.Git,
            ProjectPath = "/p/app",
            ComposePath = "/p/app/docker-compose.yml",
            ProjectUpdateMethod = DockiUp.Domain.Enums.ProjectUpdateMethod.Webhook,
            Branch = branch,
            WebhookSecret = secret,
        };
        db.ProjectInfo.Add(project);
        db.SaveChanges();
        return project;
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

    private static WebhookController Webhook(Mock<IMediator> mediator, DockiUp.Infrastructure.DockiUpDbContext db,
        string body, params (string Name, string Value)[] headers)
    {
        var controller = WithHttp(new WebhookController(mediator.Object, db));
        var request = controller.HttpContext.Request;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        foreach (var (name, value) in headers) request.Headers[name] = value;
        return controller;
    }

    private static string GitHubSignature(string secret, string body)
        => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static Mock<IMediator> QueueingMediator()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns((QueueDeploymentCommand c, CancellationToken _) => new ValueTask<DeploymentDto>(QueuedDto(c.ProjectId, c.Trigger)));
        return mediator;
    }

    private const string PushToMain = "{\"ref\":\"refs/heads/main\"}";

    [Fact]
    public async Task Webhook_Trigger_UnknownProject_ReturnsNotFound()
    {
        var mediator = QueueingMediator();

        var result = await Webhook(mediator, TestDb.Create(), PushToMain, ("X-Webhook-Secret", "topsecret")).Trigger(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
        mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("X-Webhook-Secret", "wrong")]
    [InlineData("X-Gitlab-Token", "wrong")]
    [InlineData("X-Hub-Signature-256", "sha256=deadbeef")]
    [InlineData("X-Gitea-Signature", "deadbeef")]
    [InlineData("X-Unrelated", "topsecret")]
    public async Task Webhook_Trigger_BadOrMissingCredential_ReturnsUnauthorized(string header, string value)
    {
        var db = TestDb.Create();
        var project = SeedProject(db, "main");
        var mediator = QueueingMediator();

        var result = await Webhook(mediator, db, PushToMain, (header, value)).Trigger(project.Id);

        Assert.IsType<UnauthorizedObjectResult>(result);
        mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Webhook_Trigger_EmptyStoredSecret_IsRejectedEvenWithEmptyKeyHmac()
    {
        var db = TestDb.Create();
        var project = SeedProject(db, "main", secret: "");
        var mediator = QueueingMediator();

        var result = await Webhook(mediator, db, PushToMain, ("X-Hub-Signature-256", GitHubSignature("", PushToMain))).Trigger(project.Id);

        Assert.IsType<UnauthorizedObjectResult>(result);
        mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("GitHub")]
    [InlineData("Gitea")]
    [InlineData("GiteaHex")]
    [InlineData("GitLab")]
    [InlineData("Custom")]
    public async Task Webhook_Trigger_ValidCredential_QueuesWebhookDeployment_NamedAfterProvider(string provider)
    {
        var db = TestDb.Create();
        var project = SeedProject(db, "main");
        var mediator = QueueingMediator();
        (string, string)[] headers = provider switch
        {
            "GitHub" => [("X-Hub-Signature-256", GitHubSignature("topsecret", PushToMain))],
            "Gitea" => [("X-Hub-Signature-256", GitHubSignature("topsecret", PushToMain)), ("X-Gitea-Event", "push")],
            "GiteaHex" => [("X-Gitea-Signature", GitHubSignature("topsecret", PushToMain)["sha256=".Length..])],
            "GitLab" => [("X-Gitlab-Token", "topsecret")],
            _ => [("X-Webhook-Secret", "topsecret")],
        };
        var expectedActor = (provider == "GiteaHex" ? "Gitea" : provider) + " webhook";

        var result = await Webhook(mediator, db, PushToMain, headers).Trigger(project.Id);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.IsType<DeploymentDto>(accepted.Value);
        mediator.Verify(m => m.Send(
            It.Is<QueueDeploymentCommand>(c => c.ProjectId == project.Id && c.Trigger == DeploymentTrigger.Webhook && c.ActorName == expectedActor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_Trigger_PushToOtherBranch_IsAcceptedButIgnored()
    {
        var db = TestDb.Create();
        var project = SeedProject(db, "main");
        var mediator = QueueingMediator();

        var result = await Webhook(mediator, db, "{\"ref\":\"refs/heads/feature/x\"}", ("X-Webhook-Secret", "topsecret")).Trigger(project.Id);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Contains("ignored", System.Text.Json.JsonSerializer.Serialize(accepted.Value));
        mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Webhook_Trigger_GitHubPing_ReturnsPong_WithoutDeploying()
    {
        var db = TestDb.Create();
        var project = SeedProject(db, "main");
        var mediator = QueueingMediator();
        const string ping = "{\"zen\":\"hi\"}";

        var result = await Webhook(mediator, db, ping,
            ("X-Hub-Signature-256", GitHubSignature("topsecret", ping)), ("X-GitHub-Event", "ping")).Trigger(project.Id);

        Assert.IsType<OkObjectResult>(result);
        mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Webhook_Trigger_NonJsonBody_StillDeploys()
    {
        var db = TestDb.Create();
        var project = SeedProject(db, "main");
        var mediator = QueueingMediator();

        var result = await Webhook(mediator, db, "not json", ("X-Webhook-Secret", "topsecret")).Trigger(project.Id);

        Assert.IsType<AcceptedResult>(result);
        mediator.Verify(m => m.Send(It.IsAny<QueueDeploymentCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
