using DockiUp.API.Nodes;
using DockiUp.Application.Interfaces;
using DockiUp.Domain;
using DockiUp.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace DockiUp.Tests.Api;

public class NodesControllerTests
{
    private static NodesController Build(DockiUp.Infrastructure.DockiUpDbContext db, Mock<INodeRegistry> registry,
        Mock<IActivityLogger>? activity = null, IConfiguration? config = null)
    {
        return new NodesController(db, registry.Object, new Mock<IHubContext<NodeHub>>().Object,
            config ?? new ConfigurationBuilder().Build(), (activity ?? new Mock<IActivityLogger>()).Object);
    }

    [Fact]
    public async Task List_MapsRows_WithOnlineFlagFromRegistry()
    {
        var db = TestSupport.TestDb.Create();
        var online = new Node { Name = "on", MachineName = "vm", Os = "linux", DockerVersion = "29" };
        var pending = new Node { Name = "pending" }; // no machine details -> Pending
        db.Nodes.AddRange(online, pending);
        db.SaveChanges();
        var registry = new Mock<INodeRegistry>();
        registry.Setup(r => r.OnlineLastSeen()).Returns(new Dictionary<Guid, DateTimeOffset> { [online.Id] = DateTimeOffset.UtcNow });

        var result = await Build(db, registry).List(CancellationToken.None);

        var dtos = Assert.IsAssignableFrom<IEnumerable<NodeDto>>(Assert.IsType<OkObjectResult>(result).Value).ToList();
        Assert.Equal(2, dtos.Count);
        Assert.True(dtos.Single(d => d.Id == online.Id).Online);
        Assert.False(dtos.Single(d => d.Id == pending.Id).Online);
        Assert.True(dtos.Single(d => d.Id == pending.Id).Pending);
    }

    [Fact]
    public void Draft_ReturnsGeneratedTokenAndTrimmedUrl()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DockiUp:PublicUrl"] = "https://cp.example/",
        }).Build();
        var draft = Assert.IsType<NodeDraftDto>(Assert.IsType<OkObjectResult>(Build(TestSupport.TestDb.Create(), new Mock<INodeRegistry>(), config: config).Draft()).Value);

        Assert.False(string.IsNullOrWhiteSpace(draft.SuggestedName));
        Assert.False(string.IsNullOrWhiteSpace(draft.Token));
        Assert.Equal("https://cp.example", draft.ControlPlaneUrl);
    }

    [Fact]
    public async Task Create_PersistsNode_HashesToken_LogsActivity()
    {
        var db = TestSupport.TestDb.Create();
        var activity = new Mock<IActivityLogger>();
        var result = await Build(db, new Mock<INodeRegistry>(), activity)
            .Create(new CreateNodeRequest("brave-otter", "raw-token"), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
        var node = await db.Nodes.SingleAsync();
        Assert.Equal("brave-otter", node.Name);
        Assert.Equal(NodeTokenHasher.Hash("raw-token"), node.TokenHash);
        Assert.NotEqual("raw-token", node.TokenHash);
        activity.Verify(a => a.LogAsync("node.create", "brave-otter", null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_WithoutToken_BadRequest()
    {
        var result = await Build(TestSupport.TestDb.Create(), new Mock<INodeRegistry>())
            .Create(new CreateNodeRequest("n", ""), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Delete_RemovesNode_LogsActivity()
    {
        var db = TestSupport.TestDb.Create();
        var node = new Node { Name = "gone" };
        db.Nodes.Add(node); db.SaveChanges();
        var activity = new Mock<IActivityLogger>();

        var result = await Build(db, new Mock<INodeRegistry>(), activity).Delete(node.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(db.Nodes);
        activity.Verify(a => a.LogAsync("node.delete", "gone", null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_Unknown_NotFound()
    {
        var result = await Build(TestSupport.TestDb.Create(), new Mock<INodeRegistry>()).Delete(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Ping_OfflineNode_NotFound()
    {
        var registry = new Mock<INodeRegistry>();
        string? conn = null;
        registry.Setup(r => r.TryGetConnectionId(It.IsAny<Guid>(), out conn!)).Returns(false);
        var result = await Build(TestSupport.TestDb.Create(), registry).Ping(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }
}
