using DockiUp.API.SignalR;
using DockiUp.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace DockiUp.Tests.Api;

public class DockiUpHubDisconnectTests
{
    [Fact]
    public async Task OnDisconnected_EndsLocalSessions()
    {
        var registry = new Mock<IContainerExecRegistry>();
        var session = new Mock<IContainerExecSession>();
        session.Setup(s => s.Id).Returns("s-local");
        registry.Setup(r => r.StartAsync("cid", It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<CancellationToken>())).ReturnsAsync(session.Object);
        registry.Setup(r => r.EndAsync("s-local")).Returns(Task.CompletedTask);

        var hubContext = new Mock<IHubContext<DockiUpHub>>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Client(It.IsAny<string>())).Returns(new Mock<ISingleClientProxy>().Object);
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var ctx = new Mock<HubCallerContext>();
        ctx.Setup(c => c.ConnectionId).Returns("dtest-" + Guid.NewGuid());
        ctx.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);

        var hub = new DockiUpHub(registry.Object, hubContext.Object, new Mock<INodeRpc>().Object, new ExecRelay()) { Context = ctx.Object };

        await hub.StartExec("cid", 0, 0, null);   // registers a local session on this connection
        await hub.OnDisconnectedAsync(null);       // must tear it down

        registry.Verify(r => r.EndAsync("s-local"), Times.Once);
    }

    [Fact]
    public async Task OnDisconnected_EndsNodeSessions_ViaRpc()
    {
        var rpc = new Mock<INodeRpc>();
        rpc.Setup(r => r.InvokeAsync<bool>(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var relay = new ExecRelay();
        var node = Guid.NewGuid();

        var ctx = new Mock<HubCallerContext>();
        ctx.Setup(c => c.ConnectionId).Returns("dtest-" + Guid.NewGuid());
        ctx.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);

        var hub = new DockiUpHub(new Mock<IContainerExecRegistry>().Object, new Mock<IHubContext<DockiUpHub>>().Object, rpc.Object, relay) { Context = ctx.Object };

        var sid = await hub.StartExec("cid", 80, 24, node);  // node session
        await hub.OnDisconnectedAsync(null);

        rpc.Verify(r => r.InvokeAsync<bool>(node, "EndExec", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.False(relay.TryGetExec(sid, out _, out _));
    }
}
