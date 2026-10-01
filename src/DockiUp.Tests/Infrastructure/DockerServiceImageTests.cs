using Docker.DotNet;
using Docker.DotNet.Models;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.TestSupport;
using Microsoft.Extensions.Options;
using Moq;

namespace DockiUp.Tests.Infrastructure;

/// <summary>DockerService's image-update members: service images from the daemon and the override file on disk.</summary>
public class DockerServiceImageTests
{
    private static DockerService Service(string projectsRoot, IDockerClient? docker = null)
    {
        var client = new Mock<IDockiUpDockerClient>();
        if (docker is not null) client.Setup(c => c.DockerClient).Returns(docker);
        return new DockerService(client.Object, TestDb.Create(), Options.Create(new SystemPaths { ProjectsPath = projectsRoot }),
            new Mock<IDockiUpProjectConfigurationService>().Object);
    }

    [Fact]
    public async Task ImageOverride_WriteMergeReadReset_InsideTheProjectFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "dockiup-ovr-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "app");
        Directory.CreateDirectory(project);
        try
        {
            var svc = Service(root);
            Assert.Empty(await svc.GetImageOverridesAsync(project));

            await svc.SetImageOverrideAsync(project, "web", "nginx:1.27");
            await svc.SetImageOverrideAsync(project, "db", "postgres:16");
            var file = Path.Combine(project, "dockiup.override.yml");
            Assert.True(File.Exists(file));
            Assert.Equal(new Dictionary<string, string> { ["web"] = "nginx:1.27", ["db"] = "postgres:16" }, await svc.GetImageOverridesAsync(project));

            await svc.SetImageOverrideAsync(project, "web", null);
            Assert.Equal(["db"], (await svc.GetImageOverridesAsync(project)).Keys);
            await svc.SetImageOverrideAsync(project, "db", null);
            Assert.False(File.Exists(file)); // nothing pinned: no file, compose runs without the extra -f

            await Assert.ThrowsAsync<ArgumentException>(() => svc.SetImageOverrideAsync(Path.Combine(root, ".."), "web", "x"));
            await Assert.ThrowsAsync<ArgumentException>(() => svc.GetImageOverridesAsync(Path.Combine(root, "missing")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GetServiceImages_UsesTheConfiguredReference_AndTheImagesRepoDigests()
    {
        var containers = new Mock<IContainerOperations>();
        containers.Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new() { ID = "c1", Image = "sha256:moved", Labels = new Dictionary<string, string> { ["com.docker.compose.service"] = "web" } },
                new() { ID = "c2", Image = "sha256:moved", Labels = new Dictionary<string, string> { ["com.docker.compose.service"] = "web" } },
                new() { ID = "c3", Image = "app-api", Labels = new Dictionary<string, string> { ["com.docker.compose.service"] = "api" } },
                new() { ID = "c4", Image = "x", Labels = new Dictionary<string, string>() },
            });
        containers.Setup(c => c.InspectContainerAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInspectResponse { Image = "sha256:img1", Config = new Config { Image = "nginx:latest" } });
        containers.Setup(c => c.InspectContainerAsync("c3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInspectResponse { Image = "sha256:img3", Config = new Config { Image = "app-api" } });
        var images = new Mock<IImageOperations>();
        images.Setup(i => i.InspectImageAsync("sha256:img1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageInspectResponse { RepoDigests = ["nginx@sha256:abc"] });
        images.Setup(i => i.InspectImageAsync("sha256:img3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageInspectResponse { RepoDigests = [] });
        var docker = new Mock<IDockerClient>();
        docker.Setup(d => d.Containers).Returns(containers.Object);
        docker.Setup(d => d.Images).Returns(images.Object);

        var result = await Service("/p", docker.Object).GetServiceImagesAsync("app");

        Assert.Equal(2, result.Length);
        var web = result.Single(s => s.ServiceName == "web");
        Assert.Equal("nginx:latest", web.Image);
        Assert.Equal(["nginx@sha256:abc"], web.RepoDigests);
        Assert.Empty(result.Single(s => s.ServiceName == "api").RepoDigests);
        containers.Verify(c => c.ListContainersAsync(It.Is<ContainersListParameters>(p =>
            p.Filters["label"].ContainsKey("com.docker.compose.project=app")), It.IsAny<CancellationToken>()));
    }
}
