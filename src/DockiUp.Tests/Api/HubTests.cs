using DockiUp.API.Nodes;
using DockiUp.API.SignalR;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DockiUp.Tests.Api;

public class DockiUpHubTests
{
    private static Mock<HubCallerContext> Ctx(string connId)
    {
        var ctx = new Mock<HubCallerContext>();
        ctx.Setup(c => c.ConnectionId).Returns(connId);
        ctx.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);
        return ctx;
    }

    private static (DockiUpHub hub, Mock<IContainerExecRegistry> registry, Mock<INodeRpc> rpc, ExecRelay relay) Build(string connId)
    {
        var registry = new Mock<IContainerExecRegistry>();
        var rpc = new Mock<INodeRpc>();
        var relay = new ExecRelay();
        var hubContext = new Mock<IHubContext<DockiUpHub>>();
        var clients = new Mock<IHubClients>();
        var proxy = new Mock<ISingleClientProxy>();
        clients.Setup(c => c.Client(It.IsAny<string>())).Returns(proxy.Object);
        hubContext.Setup(h => h.Clients).Returns(clients.Object);
        var hub = new DockiUpHub(registry.Object, hubContext.Object, rpc.Object, relay) { Context = Ctx(connId).Object };
        return (hub, registry, rpc, relay);
    }

    [Fact]
    public async Task StartExec_WithNodeId_RegistersRelay_AndStartsOnNode()
    {
        var (hub, registry, rpc, relay) = Build("conn1");
        var node = Guid.NewGuid();
        rpc.Setup(r => r.InvokeAsync<bool>(node, "StartExec", It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var sid = await hub.StartExec("cid", 80, 24, node);

        Assert.False(string.IsNullOrEmpty(sid));
        Assert.True(relay.TryGetExec(sid, out var gotNode, out var browser));
        Assert.Equal(node, gotNode);
        Assert.Equal("conn1", browser);
        rpc.Verify(r => r.InvokeAsync<bool>(node, "StartExec", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        registry.Verify(r => r.StartAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartExec_Local_UsesRegistry_ReturnsSessionId()
    {
        var (hub, registry, _, _) = Build("conn1");
        var session = new Mock<IContainerExecSession>();
        session.Setup(s => s.Id).Returns("local-sid");
        registry.Setup(r => r.StartAsync("cid", It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<CancellationToken>())).ReturnsAsync(session.Object);

        var sid = await hub.StartExec("cid", 0, 0, null);

        Assert.Equal("local-sid", sid);
        registry.Verify(r => r.StartAsync("cid", It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteAndResizeAndEnd_RouteToNode_WhenRelayed()
    {
        var (hub, registry, rpc, relay) = Build("conn1");
        var node = Guid.NewGuid();
        relay.RegisterExec("sid", node, "conn1");
        rpc.Setup(r => r.InvokeAsync<bool>(node, It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await hub.WriteExec("sid", "AAAA");
        await hub.ResizeExec("sid", 100, 40);
        await hub.EndExec("sid");

        rpc.Verify(r => r.InvokeAsync<bool>(node, "WriteExec", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        rpc.Verify(r => r.InvokeAsync<bool>(node, "ResizeExec", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        rpc.Verify(r => r.InvokeAsync<bool>(node, "EndExec", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(relay.TryGetExec("sid", out _, out _)); // EndExec removes it
        registry.Verify(r => r.Get(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task WriteAndResize_Local_UseRegistrySession()
    {
        var (hub, registry, _, _) = Build("conn1");
        var session = new Mock<IContainerExecSession>();
        registry.Setup(r => r.Get("sid")).Returns(session.Object);

        await hub.WriteExec("sid", Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        await hub.ResizeExec("sid", 10, 20);

        session.Verify(s => s.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
        session.Verify(s => s.ResizeAsync(10, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EndExec_Local_EndsRegistrySession()
    {
        var (hub, registry, _, _) = Build("conn1");
        registry.Setup(r => r.EndAsync("sid")).Returns(Task.CompletedTask);
        await hub.EndExec("sid");
        registry.Verify(r => r.EndAsync("sid"), Times.Once);
    }
}

public class NodeHubTests
{
    private static NodeHub Build(IServiceScopeFactory scopeFactory, INodeRegistry registry, IExecRelay relay,
        Mock<IHubContext<DockiUpHub>> containerHub, HubCallerContext context, IConfiguration? config = null)
    {
        return new NodeHub(registry, config ?? new ConfigurationBuilder().Build(), scopeFactory,
            containerHub.Object, relay, new DeployLogRelay(), NullLogger<NodeHub>.Instance)
        { Context = context };
    }

    [Fact]
    public void Heartbeat_TouchesRegistry()
    {
        var registry = new Mock<INodeRegistry>();
        var ctx = new Mock<HubCallerContext>();
        ctx.Setup(c => c.ConnectionId).Returns("c1");
        var hub = new NodeHub(registry.Object, new ConfigurationBuilder().Build(),
            TestSupport.TestDb.ScopeFactory(Guid.NewGuid().ToString()), new Mock<IHubContext<DockiUpHub>>().Object,
            new ExecRelay(), new DeployLogRelay(), NullLogger<NodeHub>.Instance) { Context = ctx.Object };

        hub.Heartbeat();

        registry.Verify(r => r.Touch("c1"), Times.Once);
    }

    [Fact]
    public async Task ExecOutput_ForwardsToBrowser_WhenRelayed()
    {
        var relay = new ExecRelay();
        relay.RegisterExec("sid", Guid.NewGuid(), "browser-conn");
        var proxy = new Mock<ISingleClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Client("browser-conn")).Returns(proxy.Object);
        var containerHub = new Mock<IHubContext<DockiUpHub>>();
        containerHub.Setup(h => h.Clients).Returns(clients.Object);

        var ctx = new Mock<HubCallerContext>();
        var hub = Build(TestSupport.TestDb.ScopeFactory(Guid.NewGuid().ToString()), new Mock<INodeRegistry>().Object, relay, containerHub, ctx.Object);

        await hub.ExecOutput("sid", "b64data");

        proxy.Verify(p => p.SendCoreAsync("ExecOutput", It.Is<object?[]>(a => (string)a[0]! == "sid" && (string)a[1]! == "b64data"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecExited_ForwardsAndRemovesRelay()
    {
        var relay = new ExecRelay();
        relay.RegisterExec("sid", Guid.NewGuid(), "browser-conn");
        var proxy = new Mock<ISingleClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Client("browser-conn")).Returns(proxy.Object);
        var containerHub = new Mock<IHubContext<DockiUpHub>>();
        containerHub.Setup(h => h.Clients).Returns(clients.Object);

        var hub = Build(TestSupport.TestDb.ScopeFactory(Guid.NewGuid().ToString()), new Mock<INodeRegistry>().Object, relay, containerHub, new Mock<HubCallerContext>().Object);

        await hub.ExecExited("sid", 0);

        proxy.Verify(p => p.SendCoreAsync("ExecExited", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(relay.TryGetExec("sid", out _, out _));
    }

    [Fact]
    public async Task Register_TokenResolvedNode_PersistsAndRegisters()
    {
        var dbName = Guid.NewGuid().ToString();
        var nodeId = Guid.NewGuid();
        var registry = new Mock<INodeRegistry>();
        var ctx = new Mock<HubCallerContext>();
        ctx.Setup(c => c.ConnectionId).Returns("c1");
        // Simulate OnConnectedAsync having resolved the node id from the token.
        ctx.Setup(c => c.Items).Returns(new Dictionary<object, object?> { ["ResolvedNodeId"] = nodeId });

        var hub = Build(TestSupport.TestDb.ScopeFactory(dbName), registry.Object, new ExecRelay(), new Mock<IHubContext<DockiUpHub>>(), ctx.Object);
        await hub.Register(new NodeRegistrationDto(nodeId.ToString(), "brave-otter", "vm", "linux", "29.0"));

        registry.Verify(r => r.Register(nodeId, "c1"), Times.Once);
        using var check = TestSupport.TestDb.Create(dbName);
        var node = await check.Nodes.SingleAsync();
        Assert.Equal(nodeId, node.Id);
        Assert.Equal("brave-otter", node.Name);
        Assert.Equal("vm", node.MachineName);
    }
}

public class DockiUpHubBroadcastServiceTests
{
    [Fact]
    public async Task Broadcast_SendsContainersChangedToAll()
    {
        var all = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(all.Object);
        var hubContext = new Mock<IHubContext<DockiUpHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var svc = new DockiUpHubBroadcastService(hubContext.Object);
        var projects = new[] { new ProjectDto { ProjectName = "p", DockerProjectName = "p", ProjectDescription = "", ManagedByDockiUp = true, Containers = [] } };

        await svc.BroadcastContainersChangedAsync(projects);

        all.Verify(a => a.SendCoreAsync(It.IsAny<string>(), It.Is<object?[]>(o => o[0] == projects), It.IsAny<CancellationToken>()), Times.Once);
    }
}
