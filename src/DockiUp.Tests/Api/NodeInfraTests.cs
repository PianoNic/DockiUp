using DockiUp.API.Nodes;
using DockiUp.API.SignalR;
using DockiUp.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace DockiUp.Tests.Api;

// Exercises the API node-infrastructure primitives: the live-connection registry, the exec relay,
// the node directory + docker-service resolvers, the RPC dispatchers, the remote docker proxy, the
// offline exception, and the node DTO records. Types under DockiUp.API.* are fully qualified where a
// bare segment could collide with DockiUp.Tests.* namespaces.
public class NodeInfraTests
{
    // ---------------------------------------------------------------------
    // NodeRegistry
    // ---------------------------------------------------------------------

    [Fact]
    public void Register_MakesNodeOnlineAndResolvable()
    {
        var registry = new NodeRegistry();
        var node = Guid.NewGuid();

        registry.Register(node, "conn-1");

        Assert.True(registry.TryGetConnectionId(node, out var connectionId));
        Assert.Equal("conn-1", connectionId);
        Assert.Contains(node, registry.OnlineLastSeen().Keys);
    }

    [Fact]
    public void Register_SameNodeTwice_UpdatesConnection_AndDropsStaleMapping()
    {
        var registry = new NodeRegistry();
        var node = Guid.NewGuid();

        registry.Register(node, "conn-1");
        registry.Register(node, "conn-2");

        Assert.True(registry.TryGetConnectionId(node, out var connectionId));
        Assert.Equal("conn-2", connectionId);

        // The stale connection mapping was dropped on re-register, so removing by it is a no-op and
        // the node stays online under the current connection.
        registry.Remove("conn-1");
        Assert.True(registry.TryGetConnectionId(node, out var stillConnected));
        Assert.Equal("conn-2", stillConnected);
    }

    [Fact]
    public void Remove_ByConnectionId_DropsNode()
    {
        var registry = new NodeRegistry();
        var node = Guid.NewGuid();
        registry.Register(node, "conn-1");

        registry.Remove("conn-1");

        Assert.False(registry.TryGetConnectionId(node, out _));
        Assert.Empty(registry.OnlineLastSeen());
    }

    [Fact]
    public void Remove_UnknownConnection_IsNoOp()
    {
        var registry = new NodeRegistry();
        var node = Guid.NewGuid();
        registry.Register(node, "conn-1");

        registry.Remove("does-not-exist");

        // The real node is untouched.
        Assert.True(registry.TryGetConnectionId(node, out _));
        Assert.Single(registry.OnlineLastSeen());
    }

    [Fact]
    public void Touch_UpdatesLastSeen_WithoutRemovingNode()
    {
        var registry = new NodeRegistry();
        var node = Guid.NewGuid();
        registry.Register(node, "conn-1");
        var before = registry.OnlineLastSeen()[node];

        registry.Touch("conn-1");

        var after = registry.OnlineLastSeen()[node];
        Assert.True(after >= before);
        Assert.True(registry.TryGetConnectionId(node, out _));
    }

    [Fact]
    public void Touch_UnknownConnection_IsNoOp()
    {
        var registry = new NodeRegistry();

        // No node registered — touching an unknown connection must not throw or create anything.
        registry.Touch("ghost");

        Assert.Empty(registry.OnlineLastSeen());
    }

    [Fact]
    public void TryGetConnectionId_FalseForUnknownNode()
    {
        var registry = new NodeRegistry();

        var found = registry.TryGetConnectionId(Guid.NewGuid(), out var connectionId);

        Assert.False(found);
        Assert.Equal(string.Empty, connectionId);
    }

    [Fact]
    public void OnlineLastSeen_ContainsAllRegisteredIds()
    {
        var registry = new NodeRegistry();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        registry.Register(a, "conn-a");
        registry.Register(b, "conn-b");

        var online = registry.OnlineLastSeen();

        Assert.Equal(2, online.Count);
        Assert.Contains(a, online.Keys);
        Assert.Contains(b, online.Keys);
    }

    // ---------------------------------------------------------------------
    // ExecRelay
    // ---------------------------------------------------------------------

    [Fact]
    public void ExecRelay_Register_Then_TryGet_RoundTrips()
    {
        var relay = new ExecRelay();
        var node = Guid.NewGuid();
        relay.RegisterExec("sess-1", node, "browser-1");

        var found = relay.TryGetExec("sess-1", out var nodeId, out var browserConnectionId);

        Assert.True(found);
        Assert.Equal(node, nodeId);
        Assert.Equal("browser-1", browserConnectionId);
    }

    [Fact]
    public void ExecRelay_TryGet_FalseAfterRemove()
    {
        var relay = new ExecRelay();
        relay.RegisterExec("sess-1", Guid.NewGuid(), "browser-1");

        relay.RemoveExec("sess-1");

        var found = relay.TryGetExec("sess-1", out var nodeId, out var browserConnectionId);
        Assert.False(found);
        Assert.Equal(Guid.Empty, nodeId);
        Assert.Equal(string.Empty, browserConnectionId);
    }

    [Fact]
    public void ExecRelay_TryGet_FalseForUnknownSession()
    {
        var relay = new ExecRelay();

        var found = relay.TryGetExec("never-registered", out var nodeId, out var browserConnectionId);

        Assert.False(found);
        Assert.Equal(Guid.Empty, nodeId);
        Assert.Equal(string.Empty, browserConnectionId);
    }

    [Fact]
    public void ExecRelay_RemoveUnknownSession_IsNoOp()
    {
        var relay = new ExecRelay();

        // Removing a session that was never registered must not throw.
        relay.RemoveExec("nothing");

        Assert.False(relay.TryGetExec("nothing", out _, out _));
    }

    // ---------------------------------------------------------------------
    // NodeDirectory
    // ---------------------------------------------------------------------

    [Fact]
    public void NodeDirectory_ReturnsRegistryOnlineKeys()
    {
        var registry = new Mock<INodeRegistry>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        registry.Setup(r => r.OnlineLastSeen())
            .Returns(new Dictionary<Guid, DateTimeOffset> { [a] = now, [b] = now });
        var directory = new NodeDirectory(registry.Object);

        var ids = directory.GetOnlineNodeIds();

        Assert.Equal(2, ids.Count);
        Assert.Contains(a, ids);
        Assert.Contains(b, ids);
        registry.Verify(r => r.OnlineLastSeen(), Times.Once);
    }

    [Fact]
    public void NodeDirectory_Empty_WhenNoNodesOnline()
    {
        var registry = new Mock<INodeRegistry>();
        registry.Setup(r => r.OnlineLastSeen()).Returns(new Dictionary<Guid, DateTimeOffset>());
        var directory = new NodeDirectory(registry.Object);

        Assert.Empty(directory.GetOnlineNodeIds());
    }

    // ---------------------------------------------------------------------
    // DockerServiceResolver
    // ---------------------------------------------------------------------

    [Fact]
    public void DockerServiceResolver_ResolvesLocal_WhenNodeIdNull()
    {
        var local = new Mock<IDockerService>();
        var hub = new Mock<IHubContext<NodeHub>>();
        var registry = new Mock<INodeRegistry>();
        var resolver = new DockerServiceResolver(local.Object, hub.Object, registry.Object, new DeployLogRelay());

        Assert.Same(local.Object, resolver.Resolve(null));
    }

    [Fact]
    public void DockerServiceResolver_ResolvesRemote_WhenNodeIdProvided()
    {
        var local = new Mock<IDockerService>();
        var hub = new Mock<IHubContext<NodeHub>>();
        var registry = new Mock<INodeRegistry>();
        var resolver = new DockerServiceResolver(local.Object, hub.Object, registry.Object, new DeployLogRelay());

        var resolved = resolver.Resolve(Guid.NewGuid());

        Assert.NotNull(resolved);
        Assert.IsType<RemoteDockerService>(resolved);
        Assert.NotSame(local.Object, resolved);
    }

    // ---------------------------------------------------------------------
    // NodeRpc
    // ---------------------------------------------------------------------

    [Fact]
    public async Task NodeRpc_InvokeAsync_ThrowsNodeOffline_WhenNodeNotRegistered()
    {
        var hub = new Mock<IHubContext<NodeHub>>();
        var registry = new Mock<INodeRegistry>();
        string connectionId = string.Empty;
        registry.Setup(r => r.TryGetConnectionId(It.IsAny<Guid>(), out connectionId)).Returns(false);
        var rpc = new NodeRpc(hub.Object, registry.Object);
        var node = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<NodeOfflineException>(
            () => rpc.InvokeAsync<bool>(node, "AnyMethod", new object?[] { "arg" }, CancellationToken.None));

        Assert.Equal(node, ex.NodeId);
    }

    // ---------------------------------------------------------------------
    // NodeOfflineException
    // ---------------------------------------------------------------------

    [Fact]
    public void NodeOfflineException_CarriesNodeId_AndDescriptiveMessage()
    {
        var node = Guid.NewGuid();

        var ex = new NodeOfflineException(node);

        Assert.Equal(node, ex.NodeId);
        Assert.Contains(node.ToString(), ex.Message);
        Assert.Contains("not connected", ex.Message);
        Assert.IsAssignableFrom<Exception>(ex);
    }

    // ---------------------------------------------------------------------
    // RemoteDockerService — every method funnels through Node(), which throws when offline.
    // ---------------------------------------------------------------------

    private static RemoteDockerService OfflineRemote(Guid node)
    {
        var hub = new Mock<IHubContext<NodeHub>>();
        var registry = new Mock<INodeRegistry>();
        string connectionId = string.Empty;
        registry.Setup(r => r.TryGetConnectionId(It.IsAny<Guid>(), out connectionId)).Returns(false);
        return new RemoteDockerService(node, hub.Object, registry.Object, new DeployLogRelay());
    }

    [Fact]
    public async Task RemoteDockerService_GetProjectsAsync_ThrowsNodeOffline_WhenOffline()
    {
        var node = Guid.NewGuid();
        var svc = OfflineRemote(node);

        var ex = await Assert.ThrowsAsync<NodeOfflineException>(() => svc.GetProjectsAsync());
        Assert.Equal(node, ex.NodeId);
    }

    [Fact]
    public async Task RemoteDockerService_GetRawProjectsAsync_ThrowsNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.GetRawProjectsAsync());
    }

    [Fact]
    public async Task RemoteDockerService_GetProjectByDockerNameAsync_ThrowsNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.GetProjectByDockerNameAsync("proj"));
    }

    [Fact]
    public async Task RemoteDockerService_InspectContainerAsync_ThrowsNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.InspectContainerAsync("cid"));
    }

    [Fact]
    public async Task RemoteDockerService_StartProjectAsync_ThrowsNodeOffline_WhenOffline()
    {
        var node = Guid.NewGuid();
        var svc = OfflineRemote(node);

        var ex = await Assert.ThrowsAsync<NodeOfflineException>(() => svc.StartProjectAsync("myapp"));
        Assert.Equal(node, ex.NodeId);
    }

    [Fact]
    public async Task RemoteDockerService_StopProjectAsync_ThrowsNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.StopProjectAsync("myapp"));
    }

    [Fact]
    public async Task RemoteDockerService_RestartProjectAsync_ThrowsNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.RestartProjectAsync("myapp"));
    }

    [Fact]
    public async Task RemoteDockerService_PipelineSteps_ThrowNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());

        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.SyncRepositoryAsync("/p/app", "main", null, _ => Task.CompletedTask));
        await Assert.ThrowsAsync<NodeOfflineException>(() =>
            svc.ComposeUpAsync(new DockiUp.Application.Dtos.ComposeTarget("/p/app", "/p/app/c.yml", "app"), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task DeployLogRelay_RoutesLinesToRegisteredRun_AndDropsAfterRemove()
    {
        var relay = new DeployLogRelay();
        var lines = new List<string>();
        var runId = relay.Register(line => { lines.Add(line); return Task.CompletedTask; });

        await relay.WriteAsync(runId, "one");
        await relay.WriteAsync("unknown-run", "ignored");
        relay.Remove(runId);
        await relay.WriteAsync(runId, "late");

        Assert.Equal(["one"], lines);
    }

    [Fact]
    public async Task RemoteDockerService_StartContainerAsync_ThrowsNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.StartContainerAsync("cid"));
    }

    [Fact]
    public async Task RemoteDockerService_StopContainerAsync_ThrowsNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.StopContainerAsync("cid"));
    }

    [Fact]
    public async Task RemoteDockerService_RestartContainerAsync_ThrowsNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.RestartContainerAsync("cid"));
    }

    [Fact]
    public async Task RemoteDockerService_GetContainerLogsAsync_ThrowsNodeOffline_WhenOffline()
    {
        var svc = OfflineRemote(Guid.NewGuid());
        await Assert.ThrowsAsync<NodeOfflineException>(() => svc.GetContainerLogsAsync("cid", 100));
    }

    // ---------------------------------------------------------------------
    // NodeDtos — record construction + member read-back.
    // ---------------------------------------------------------------------

    [Fact]
    public void NodeRegistrationDto_ExposesMembers()
    {
        var dto = new NodeRegistrationDto("id-123", "web-1", "host-a", "linux", "27.0.1");

        Assert.Equal("id-123", dto.Id);
        Assert.Equal("web-1", dto.Name);
        Assert.Equal("host-a", dto.MachineName);
        Assert.Equal("linux", dto.Os);
        Assert.Equal("27.0.1", dto.DockerVersion);
    }

    [Fact]
    public void NodeDto_ExposesMembers()
    {
        var id = Guid.NewGuid();
        var first = DateTimeOffset.UtcNow.AddDays(-1);
        var last = DateTimeOffset.UtcNow;

        var dto = new NodeDto(id, "web-1", "host-a", "linux", "27.0.1", Online: true, Pending: false, first, last);

        Assert.Equal(id, dto.Id);
        Assert.Equal("web-1", dto.Name);
        Assert.Equal("host-a", dto.MachineName);
        Assert.Equal("linux", dto.Os);
        Assert.Equal("27.0.1", dto.DockerVersion);
        Assert.True(dto.Online);
        Assert.False(dto.Pending);
        Assert.Equal(first, dto.FirstSeenAt);
        Assert.Equal(last, dto.LastSeenAt);
    }

    [Fact]
    public void NodePingResultDto_ExposesMembers()
    {
        var dto = new NodePingResultDto("pong", 42);

        Assert.Equal("pong", dto.Reply);
        Assert.Equal(42, dto.RoundTripMs);
    }

    [Fact]
    public void NodeDraftDto_ExposesMembers_IncludingNullableServerUrl()
    {
        var dto = new NodeDraftDto("suggested", "tok-abc", "https://control.example");
        Assert.Equal("suggested", dto.SuggestedName);
        Assert.Equal("tok-abc", dto.Token);
        Assert.Equal("https://control.example", dto.ServerUrl);

        var withoutUrl = new NodeDraftDto("s", "t", null);
        Assert.Null(withoutUrl.ServerUrl);
    }

    [Fact]
    public void CreateNodeRequest_ExposesMembers()
    {
        var dto = new CreateNodeRequest("web-1", "tok-xyz");

        Assert.Equal("web-1", dto.Name);
        Assert.Equal("tok-xyz", dto.Token);
    }

    [Fact]
    public void NodeDto_ValueEquality_HoldsForRecords()
    {
        var id = Guid.NewGuid();
        var first = DateTimeOffset.UtcNow.AddDays(-1);
        var last = DateTimeOffset.UtcNow;
        var a = new NodeDto(id, "web-1", "host-a", "linux", "27.0.1", true, false, first, last);
        var b = new NodeDto(id, "web-1", "host-a", "linux", "27.0.1", true, false, first, last);

        Assert.Equal(a, b);
    }
}
