using DockiUp.API.Nodes;
using DockiUp.Application.Dtos;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace DockiUp.Tests.Api;

/// <summary>The node-side calls RemoteDockerService makes for monitoring and housekeeping.</summary>
public class RemoteMonitoringTests
{
    private static (RemoteDockerService, Mock<ISingleClientProxy>) Online(Guid node)
    {
        var proxy = new Mock<ISingleClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Client("conn")).Returns(proxy.Object);
        var hub = new Mock<IHubContext<NodeHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        var registry = new Mock<INodeRegistry>();
        var connectionId = "conn";
        registry.Setup(r => r.TryGetConnectionId(node, out connectionId)).Returns(true);
        return (new RemoteDockerService(node, hub.Object, registry.Object, new DeployLogRelay()), proxy);
    }

    [Fact]
    public async Task PlainLogs_UseTheOriginalHandler_SoOlderNodesKeepWorking()
    {
        var (svc, proxy) = Online(Guid.NewGuid());
        proxy.Setup(p => p.InvokeCoreAsync<string>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).ReturnsAsync("log");

        await svc.GetContainerLogsAsync("cid", new ContainerLogOptions(50));
        await svc.GetContainerLogsAsync("cid", new ContainerLogOptions(50, Timestamps: true));

        proxy.Verify(p => p.InvokeCoreAsync<string>("GetContainerLogs", It.Is<object?[]>(a => (string)a[0]! == "cid" && (int?)a[1] == 50), It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(p => p.InvokeCoreAsync<string>("GetContainerLogsWithOptions", It.Is<object?[]>(a => ((ContainerLogOptions)a[1]!).Timestamps), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Stats_AreStampedWithTheNode()
    {
        var node = Guid.NewGuid();
        var (svc, proxy) = Online(node);
        proxy.Setup(p => p.InvokeCoreAsync<ContainerStatsDto[]>("GetContainerStats", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ContainerStatsDto("c", "web", "proj", 1, 2, 3, 4, 5, DateTime.UtcNow)]);

        var stats = await svc.GetContainerStatsAsync();

        Assert.Equal(node, Assert.Single(stats).NodeId);
    }

    [Fact]
    public async Task Resources_RemoveAndPrune_InvokeTheNode()
    {
        var (svc, proxy) = Online(Guid.NewGuid());
        var request = new PruneRequest(Images: true);
        proxy.Setup(p => p.InvokeCoreAsync<bool>("RemoveResource", It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        proxy.Setup(p => p.InvokeCoreAsync<PruneResultDto>("PruneResources", It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PruneResultDto(0, 1, 0, 0, 5));
        proxy.Setup(p => p.InvokeCoreAsync<DockerResourcesDto>("GetResources", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DockerResourcesDto([], [], [], new DiskUsageDto(null, null, null, null, null, null)));

        await svc.RemoveResourceAsync(ResourceKind.Network, "net");
        Assert.Equal(5, (await svc.PruneAsync(request)).SpaceReclaimed);
        Assert.Empty((await svc.GetResourcesAsync()).Images);

        proxy.Verify(p => p.InvokeCoreAsync<bool>("RemoveResource", It.Is<object?[]>(a => (ResourceKind)a[0]! == ResourceKind.Network && (string)a[1]! == "net"), It.IsAny<CancellationToken>()));
        proxy.Verify(p => p.InvokeCoreAsync<PruneResultDto>("PruneResources", It.Is<object?[]>(a => Equals(a[0], request)), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Offline_NodeThrows()
    {
        var hub = new Mock<IHubContext<NodeHub>>();
        var registry = new Mock<INodeRegistry>();
        var none = string.Empty;
        registry.Setup(r => r.TryGetConnectionId(It.IsAny<Guid>(), out none)).Returns(false);
        var svc = new RemoteDockerService(Guid.NewGuid(), hub.Object, registry.Object, new DeployLogRelay());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.GetContainerStatsAsync());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.PruneAsync(new PruneRequest()));
    }
}
