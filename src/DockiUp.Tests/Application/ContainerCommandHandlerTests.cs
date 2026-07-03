using DockiUp.Application.Commands;
using DockiUp.Application.Interfaces;
using Moq;

namespace DockiUp.Tests.Application;

/// <summary>The container command handlers must resolve the docker service by the command's NodeId
/// and delegate the matching call. This proves the multi-server routing at the handler level.</summary>
public class ContainerCommandHandlerTests
{
    private static (Mock<IDockerServiceResolver> resolver, Mock<IDockerService> docker) BuildResolver(Guid? expectedNode)
    {
        var docker = new Mock<IDockerService>();
        var resolver = new Mock<IDockerServiceResolver>();
        resolver.Setup(r => r.Resolve(expectedNode)).Returns(docker.Object);
        return (resolver, docker);
    }

    [Fact]
    public async Task StartContainer_RoutesToResolvedNode()
    {
        var node = Guid.NewGuid();
        var (resolver, docker) = BuildResolver(node);
        var handler = new StartContainerCommandHandler(resolver.Object);

        await handler.Handle(new StartContainerCommand("cid", node), CancellationToken.None);

        resolver.Verify(r => r.Resolve(node), Times.Once);
        docker.Verify(d => d.StartContainerAsync("cid"), Times.Once);
    }

    [Fact]
    public async Task StopContainer_RoutesLocally_WhenNodeIdNull()
    {
        var (resolver, docker) = BuildResolver(null);
        var handler = new StopContainerCommandHandler(resolver.Object);

        await handler.Handle(new StopContainerCommand("cid"), CancellationToken.None);

        resolver.Verify(r => r.Resolve(null), Times.Once);
        docker.Verify(d => d.StopContainerAsync("cid"), Times.Once);
    }

    [Fact]
    public async Task RestartContainer_RoutesToResolvedNode()
    {
        var node = Guid.NewGuid();
        var (resolver, docker) = BuildResolver(node);
        var handler = new RestartContainerCommandHandler(resolver.Object);

        await handler.Handle(new RestartContainerCommand("cid", node), CancellationToken.None);

        docker.Verify(d => d.RestartContainerAsync("cid"), Times.Once);
    }
}
