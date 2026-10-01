using DockiUp.Application.Compose;
using DockiUp.Application.Dtos;
using YamlDotNet.RepresentationModel;

namespace DockiUp.Tests.Application;

/// <summary>`docker run` -> compose conversion and the image form -> compose generator. Every result is
/// parsed back as YAML, so quoting mistakes show up as test failures.</summary>
public class DockerRunConverterTests
{
    private static YamlMappingNode Service(string compose, out string name)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(compose));
        var services = (YamlMappingNode)((YamlMappingNode)stream.Documents[0].RootNode)["services"];
        var entry = Assert.Single(services.Children);
        name = ((YamlScalarNode)entry.Key).Value!;
        return (YamlMappingNode)entry.Value;
    }

    private static YamlMappingNode Root(string compose)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(compose));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static string Scalar(YamlMappingNode node, string key) => ((YamlScalarNode)node[key]).Value!;

    private static string[] List(YamlMappingNode node, string key)
        => [.. ((YamlSequenceNode)node[key]).Children.Select(c => ((YamlScalarNode)c).Value!)];

    [Fact]
    public void Converts_CommonFlags()
    {
        var result = DockerRunConverter.Convert(
            "docker run -d --name my-web -p 8080:80 -p 443:443/tcp -v /srv/www:/usr/share/nginx/html:ro -v data:/data " +
            "-e TZ=Europe/Zurich -e DEBUG --env-file ./app.env --restart unless-stopped -l traefik.enable=true nginx:1.27");

        Assert.Empty(result.Warnings);
        var svc = Service(result.Compose, out var name);
        Assert.Equal("my-web", name);
        Assert.Equal("nginx:1.27", Scalar(svc, "image"));
        Assert.Equal("my-web", Scalar(svc, "container_name"));
        Assert.Equal(["8080:80", "443:443/tcp"], List(svc, "ports"));
        Assert.Equal(["/srv/www:/usr/share/nginx/html:ro", "data:/data"], List(svc, "volumes"));
        Assert.Equal(["TZ=Europe/Zurich", "DEBUG"], List(svc, "environment"));
        Assert.Equal(["./app.env"], List(svc, "env_file"));
        Assert.Equal("unless-stopped", Scalar(svc, "restart"));
        Assert.Equal(["traefik.enable=true"], List(svc, "labels"));
        // Named volumes are declared at the top level; host paths are not.
        var volumes = (YamlMappingNode)Root(result.Compose)["volumes"];
        Assert.Equal(["data"], volumes.Children.Keys.Select(k => ((YamlScalarNode)k).Value));
    }

    [Fact]
    public void LongFlagsWithEquals_AndGluedShortValues()
    {
        var result = DockerRunConverter.Convert("docker run --name=app --publish=9000:9000 -p5000:5000 -eA=1 --volume=/a:/a redis");

        var svc = Service(result.Compose, out _);
        Assert.Equal(["9000:9000", "5000:5000"], List(svc, "ports"));
        Assert.Equal(["A=1"], List(svc, "environment"));
        Assert.Equal(["/a:/a"], List(svc, "volumes"));
    }

    [Fact]
    public void CommandAndArgs_AfterTheImage_AreKept()
    {
        var result = DockerRunConverter.Convert("docker run --rm alpine:3 sh -c 'echo \"hi there\" && sleep 5'");

        var svc = Service(result.Compose, out var name);
        Assert.Equal("alpine", name); // named after the image without a --name
        Assert.Equal(["sh", "-c", "echo \"hi there\" && sleep 5"], List(svc, "command"));
        Assert.Contains(result.Warnings, w => w.Contains("--rm"));
    }

    [Fact]
    public void UserNetwork_IsJoinedAsExternal_BuiltInModesAreNetworkMode()
    {
        var joined = DockerRunConverter.Convert("docker run --network proxy nginx");
        Assert.Equal(["proxy"], List(Service(joined.Compose, out _), "networks"));
        var external = (YamlMappingNode)((YamlMappingNode)Root(joined.Compose)["networks"])["proxy"];
        Assert.Equal("true", Scalar(external, "external"));

        var host = DockerRunConverter.Convert("docker run --net=host nginx");
        Assert.Equal("host", Scalar(Service(host.Compose, out _), "network_mode"));
        Assert.False(Root(host.Compose).Children.ContainsKey(new YamlScalarNode("networks")));
    }

    [Fact]
    public void UnsupportedFlags_AreReported_NotDropped()
    {
        var result = DockerRunConverter.Convert("docker run --gpus all --cap-add NET_ADMIN --init -p 80:80 nginx");

        Assert.Contains(result.Warnings, w => w.Contains("--gpus all"));
        Assert.Contains(result.Warnings, w => w.Contains("--cap-add NET_ADMIN"));
        Assert.Contains(result.Warnings, w => w.Contains("--init"));
        // A value-taking unknown flag doesn't swallow the image.
        Assert.Equal("nginx", Scalar(Service(result.Compose, out _), "image"));
    }

    [Fact]
    public void BundledShortBooleans_AndMiscFlags()
    {
        var result = DockerRunConverter.Convert("docker run -dit --privileged -u 1000:1000 -w /app -h box --entrypoint '/bin/sh -c' busybox");

        var svc = Service(result.Compose, out _);
        Assert.Equal("true", Scalar(svc, "stdin_open"));
        Assert.Equal("true", Scalar(svc, "tty"));
        Assert.Equal("true", Scalar(svc, "privileged"));
        Assert.Equal("1000:1000", Scalar(svc, "user"));
        Assert.Equal("/app", Scalar(svc, "working_dir"));
        Assert.Equal("box", Scalar(svc, "hostname"));
        Assert.Equal(["/bin/sh", "-c"], List(svc, "entrypoint"));
    }

    [Fact]
    public void MultilineCommands_WithBackslashContinuations()
    {
        var result = DockerRunConverter.Convert("docker run -d \\\n  -p 3000:3000 \\\r\n  --name grafana \\\n  grafana/grafana");

        var svc = Service(result.Compose, out var name);
        Assert.Equal("grafana", name);
        Assert.Equal("grafana/grafana", Scalar(svc, "image"));
        Assert.Equal(["3000:3000"], List(svc, "ports"));
    }

    [Fact]
    public void DockerContainerRun_IsAccepted()
        => Assert.Equal("nginx", Scalar(Service(DockerRunConverter.Convert("docker container run nginx").Compose, out _), "image"));

    [Theory]
    [InlineData("docker ps")]
    [InlineData("docker run -p 80:80")]
    [InlineData("docker run --name")]
    [InlineData("docker run 'nginx")]
    [InlineData("")]
    public void Invalid_Throws(string command)
        => Assert.Throws<ArgumentException>(() => DockerRunConverter.Convert(command));

    [Fact]
    public void ValuesThatLookLikeYamlTypes_StayStrings()
    {
        var result = DockerRunConverter.Convert("docker run -p 22:22 -e ENABLED=yes -l 'note=a: b # c' nginx");

        var svc = Service(result.Compose, out _);
        Assert.Equal(["22:22"], List(svc, "ports"));
        Assert.Equal(["note=a: b # c"], List(svc, "labels"));
        Assert.Contains("\"22:22\"", result.Compose);
    }
}

public class ImageComposeGeneratorTests
{
    private static YamlMappingNode Service(string compose)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(compose));
        var services = (YamlMappingNode)((YamlMappingNode)stream.Documents[0].RootNode)["services"];
        return (YamlMappingNode)Assert.Single(services.Children).Value;
    }

    [Fact]
    public void Generates_AllFields()
    {
        var result = ImageComposeGenerator.Generate(new ImageProjectDto
        {
            Image = "ghcr.io/owner/app",
            Tag = "v2",
            Ports = ["8080:80", " ", "127.0.0.1:9090:90/udp"],
            Volumes = ["./data:/data", "cache:/cache:ro"],
            Environment = ["A=1", "B=two words"],
            Restart = "always",
        });

        var svc = Service(result.Compose);
        Assert.Contains("  app:\n", result.Compose); // service named after the image
        Assert.Equal("ghcr.io/owner/app:v2", ((YamlScalarNode)svc["image"]).Value);
        Assert.Equal(2, ((YamlSequenceNode)svc["ports"]).Children.Count); // blank rows ignored
        Assert.Equal("always", ((YamlScalarNode)svc["restart"]).Value);
        Assert.Contains("\"B=two words\"", result.Compose);
        Assert.Contains("volumes:\n  \"cache\": {}", result.Compose);
    }

    [Fact]
    public void ImageOnly_IsEnough()
    {
        var result = ImageComposeGenerator.Generate(new ImageProjectDto { Image = "nginx" });
        Assert.Equal("services:\n  nginx:\n    image: \"nginx\"\n", result.Compose);
    }

    [Fact]
    public void RegistryPort_IsNotMistakenForATag()
        => Assert.Contains("\"localhost:5000/app:1\"", ImageComposeGenerator.Generate(new ImageProjectDto { Image = "localhost:5000/app", Tag = "1" }).Compose);

    [Theory]
    [InlineData("Nginx", null)]          // uppercase
    [InlineData("nginx:1", "2")]         // tag twice
    [InlineData("nginx", "bad tag")]
    [InlineData("", null)]
    public void InvalidImageOrTag_Throws(string image, string? tag)
        => Assert.Throws<ArgumentException>(() => ImageComposeGenerator.Generate(new ImageProjectDto { Image = image, Tag = tag }));

    [Theory]
    [InlineData("80:80:80:80", null, null, null)]
    [InlineData(null, "/only-target", null, null)]
    [InlineData(null, "a:relative", null, null)]
    [InlineData(null, "a:/b:rx", null, null)]
    [InlineData(null, null, "1BAD=x", null)]
    [InlineData(null, null, null, "sometimes")]
    public void InvalidRows_Throw(string? port, string? volume, string? env, string? restart)
        => Assert.Throws<ArgumentException>(() => ImageComposeGenerator.Generate(new ImageProjectDto
        {
            Image = "nginx",
            Ports = port is null ? [] : [port],
            Volumes = volume is null ? [] : [volume],
            Environment = env is null ? [] : [env],
            Restart = restart,
        }));

    [Fact]
    public void OnFailureWithRetries_IsAccepted()
        => Assert.Contains("on-failure:3", ImageComposeGenerator.Generate(new ImageProjectDto { Image = "nginx", Restart = "on-failure:3" }).Compose);
}
