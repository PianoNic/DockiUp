using Docker.DotNet;
using Docker.DotNet.Models;
using DockiUp.Application.Enums;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Domain;
using DockiUp.Domain.Enums;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.TestSupport;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Infrastructure;

public class DockerServiceTests
{
    private static ContainerListResponse Container(string id, string project, string service, string state = "running", string status = "Up 2s")
        => new()
        {
            ID = id,
            Names = new List<string> { "/" + project + "-" + service + "-1" },
            State = state,
            Status = status,
            Labels = new Dictionary<string, string>
            {
                ["com.docker.compose.project"] = project,
                ["com.docker.compose.service"] = service,
            },
        };

    private static (DockerService svc, Mock<IContainerOperations> containers, DockiUp.Infrastructure.DockiUpDbContext db) Build(
        IList<ContainerListResponse> list)
    {
        var containers = new Mock<IContainerOperations>();
        containers.Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);
        var docker = new Mock<IDockerClient>();
        docker.Setup(d => d.Containers).Returns(containers.Object);
        var client = new Mock<IDockiUpDockerClient>();
        client.Setup(c => c.DockerClient).Returns(docker.Object);
        var db = TestDb.Create();
        var svc = new DockerService(client.Object, db, Options.Create(new SystemPaths { ProjectsPath = "/p" }));
        return (svc, containers, db);
    }

    [Fact]
    public async Task GetRawProjects_GroupsByComposeProject_MapsContainers_Unmanaged()
    {
        var (svc, _, _) = Build(new[]
        {
            Container("a", "proj", "web"),
            Container("b", "proj", "db", state: "exited", status: "Exited"),
            new ContainerListResponse { ID = "c", Names = new List<string> { "/loose" }, State = "running", Labels = new Dictionary<string, string>() }, // no compose label -> excluded
        });

        var projects = await svc.GetRawProjectsAsync();

        var p = Assert.Single(projects);
        Assert.Equal("proj", p.DockerProjectName);
        Assert.False(p.ManagedByDockiUp);
        Assert.Null(p.Id);
        Assert.Equal(2, p.Containers.Length);
        Assert.Contains(p.Containers, c => c.Name == "proj-web-1" && c.State == UpdateMethodType.Running && c.ServiceName == "web");
        Assert.Contains(p.Containers, c => c.State == UpdateMethodType.Stopped);
    }

    [Fact]
    public async Task GetProjects_ReconcilesWithDb_SetsIdNodeManaged()
    {
        var (svc, _, db) = Build(new[] { Container("a", "proj", "web") });
        var node = Guid.NewGuid();
        db.ProjectInfo.Add(new ProjectInfo
        {
            ProjectName = "Nice Name", DockerProjectName = "proj", ProjectOrigin = ProjectOriginType.Compose,
            ProjectPath = "/pp", ComposePath = "/pp/c", ProjectUpdateMethod = ProjectUpdateMethod.Manual, NodeId = node,
        });
        db.SaveChanges();

        var p = Assert.Single(await svc.GetProjectsAsync());
        Assert.True(p.ManagedByDockiUp);
        Assert.Equal("Nice Name", p.ProjectName);
        Assert.Equal(node, p.NodeId);
        Assert.Equal("/pp", p.ProjectPath);
    }

    [Fact]
    public async Task GetProjects_TolueratesDuplicateDockerNames_NoThrow()
    {
        var (svc, _, db) = Build(new[] { Container("a", "dup", "web") });
        // Two rows normalizing to the same docker name must not crash the listing (last wins).
        db.ProjectInfo.Add(new ProjectInfo { ProjectName = "First", DockerProjectName = "dup", ProjectOrigin = ProjectOriginType.Compose, ProjectPath = "/1", ComposePath = "/1/c", ProjectUpdateMethod = ProjectUpdateMethod.Manual });
        db.ProjectInfo.Add(new ProjectInfo { ProjectName = "Second", DockerProjectName = "dup", ProjectOrigin = ProjectOriginType.Compose, ProjectPath = "/2", ComposePath = "/2/c", ProjectUpdateMethod = ProjectUpdateMethod.Manual });
        db.SaveChanges();

        var projects = await svc.GetProjectsAsync();
        var p = Assert.Single(projects);
        Assert.True(p.ManagedByDockiUp);
        Assert.Equal("Second", p.ProjectName); // last wins
    }

    [Fact]
    public async Task GetProjectByDockerName_ReturnsMatch_OrNull()
    {
        var (svc, _, _) = Build(new[] { Container("a", "proj", "web") });
        Assert.NotNull(await svc.GetProjectByDockerNameAsync("proj"));
        Assert.Null(await svc.GetProjectByDockerNameAsync("nope"));
    }

    [Fact]
    public async Task InspectContainer_FindsByIdAndPrefix_NullWhenMissing()
    {
        var (svc, _, _) = Build(new[] { Container("abcdef123456", "proj", "web") });
        Assert.NotNull(await svc.InspectContainerAsync("abcdef123456"));
        Assert.NotNull(await svc.InspectContainerAsync("abcdef")); // prefix match
        Assert.Null(await svc.InspectContainerAsync("zzzz"));
    }

    [Fact]
    public async Task ContainerLifecycle_DelegatesToDaemon()
    {
        var (svc, containers, _) = Build(Array.Empty<ContainerListResponse>());
        containers.Setup(c => c.StartContainerAsync("x", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        containers.Setup(c => c.StopContainerAsync("x", It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await svc.StartContainerAsync("x");
        await svc.StopContainerAsync("x");
        await svc.RestartContainerAsync("x");

        containers.Verify(c => c.StartContainerAsync("x", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()), Times.Once);
        containers.Verify(c => c.StopContainerAsync("x", It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()), Times.Once);
        containers.Verify(c => c.RestartContainerAsync("x", It.IsAny<ContainerRestartParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContainerLogs_DecodesMultiplexedStream()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("log line\n");
        var frame = new byte[8 + payload.Length];
        frame[0] = 1; // stdout
        frame[4] = (byte)((payload.Length >> 24) & 0xFF);
        frame[5] = (byte)((payload.Length >> 16) & 0xFF);
        frame[6] = (byte)((payload.Length >> 8) & 0xFF);
        frame[7] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 8, payload.Length);

        var (svc, containers, _) = Build(Array.Empty<ContainerListResponse>());
#pragma warning disable CS0618
        containers.Setup(c => c.GetContainerLogsAsync("cid", It.IsAny<ContainerLogsParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(frame));
#pragma warning restore CS0618

        var logs = await svc.GetContainerLogsAsync("cid", tail: 50);
        Assert.Equal("log line\n", logs);
    }
}
