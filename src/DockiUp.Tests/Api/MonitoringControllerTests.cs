using DockiUp.API.Controllers;
using DockiUp.Application.Dtos;
using DockiUp.Application.Monitoring;
using DockiUp.Application.Queries;
using DockiUp.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace DockiUp.Tests.Api;

/// <summary>Stats and resources controllers, and the new log filter parameters, delegate to the mediator.</summary>
public class MonitoringControllerTests
{
    private static T WithHttp<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Fact]
    public async Task GetContainerLogs_PassesStreamAndTimestampFilters()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetContainerLogsQuery>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<string>("x"));

        await WithHttp(new ContainerController(mediator.Object)).GetContainerLogs("cid", 0, null, stdout: false, stderr: true, timestamps: true);

        mediator.Verify(m => m.Send(It.Is<GetContainerLogsQuery>(q =>
            q.Tail == 0 && !q.Stdout && q.Stderr && q.Timestamps), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContainerLogs_DefaultsKeepTheOldBehaviour()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetContainerLogsQuery>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<string>("x"));

        await WithHttp(new ContainerController(mediator.Object)).GetContainerLogs("cid");

        mediator.Verify(m => m.Send(It.Is<GetContainerLogsQuery>(q =>
            q.Tail == 200 && q.Stdout && q.Stderr && !q.Timestamps), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Stats_Current_And_History()
    {
        var node = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        ContainerStatsDto[] live = [new("c", "web", "proj", 1, 2, 3, 4, 5, DateTime.UtcNow)];
        ContainerStatsPointDto[] history = [new(DateTime.UtcNow, 1, 2, 3, 4, 5)];
        mediator.Setup(m => m.Send(It.IsAny<GetContainerStatsQuery>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<ContainerStatsDto[]>(live));
        mediator.Setup(m => m.Send(It.IsAny<GetContainerStatsHistoryQuery>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<ContainerStatsPointDto[]>(history));
        var controller = WithHttp(new StatsController(mediator.Object));

        Assert.Same(live, Assert.IsType<OkObjectResult>((await controller.GetContainerStats("proj", null, node)).Result).Value);
        Assert.Same(history, Assert.IsType<OkObjectResult>((await controller.GetContainerStatsHistory("web", node, 6)).Result).Value);
        mediator.Verify(m => m.Send(new GetContainerStatsQuery("proj", null, node), It.IsAny<CancellationToken>()));
        mediator.Verify(m => m.Send(new GetContainerStatsHistoryQuery("web", node, 6), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Resources_ListRemovePruneScheduleRun()
    {
        var node = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        var resources = new DockerResourcesDto([], [], [], new DiskUsageDto(null, null, null, null, null, null));
        var pruned = new PruneResultDto(1, 1, 0, 1, 99);
        var schedule = new CleanupScheduleDto(node, CleanupFrequency.Daily, 3, 0, false, null, null);
        mediator.Setup(m => m.Send(It.IsAny<GetResourcesQuery>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<DockerResourcesDto>(resources));
        mediator.Setup(m => m.Send(It.IsAny<RemoveResourceCommand>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<Unit>(Unit.Value));
        mediator.Setup(m => m.Send(It.IsAny<PruneResourcesCommand>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<PruneResultDto>(pruned));
        mediator.Setup(m => m.Send(It.IsAny<RunCleanupCommand>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<PruneResultDto>(pruned));
        mediator.Setup(m => m.Send(It.IsAny<GetCleanupScheduleQuery>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<CleanupScheduleDto>(schedule));
        mediator.Setup(m => m.Send(It.IsAny<SaveCleanupScheduleCommand>(), It.IsAny<CancellationToken>())).Returns(new ValueTask<CleanupScheduleDto>(schedule));
        var controller = WithHttp(new ResourcesController(mediator.Object));
        var request = new PruneRequest(Images: true);
        var save = new SaveCleanupScheduleCommand(node, CleanupFrequency.Daily, 3, 0, false);

        Assert.Same(resources, Assert.IsType<OkObjectResult>((await controller.GetResources(node)).Result).Value);
        Assert.IsType<NoContentResult>(await controller.RemoveResource(ResourceKind.Volume, "data", node, "data"));
        Assert.Same(pruned, Assert.IsType<OkObjectResult>((await controller.PruneResources(request, node)).Result).Value);
        Assert.Same(schedule, Assert.IsType<OkObjectResult>((await controller.GetCleanupSchedule(node)).Result).Value);
        Assert.Same(schedule, Assert.IsType<OkObjectResult>((await controller.SaveCleanupSchedule(save)).Result).Value);
        Assert.Same(pruned, Assert.IsType<OkObjectResult>((await controller.RunCleanup(node)).Result).Value);

        mediator.Verify(m => m.Send(new RemoveResourceCommand(ResourceKind.Volume, "data", node, "data"), It.IsAny<CancellationToken>()));
        mediator.Verify(m => m.Send(new PruneResourcesCommand(request, node), It.IsAny<CancellationToken>()));
        mediator.Verify(m => m.Send(new RunCleanupCommand(node, false), It.IsAny<CancellationToken>()));
        mediator.Verify(m => m.Send(save, It.IsAny<CancellationToken>()));
    }
}
