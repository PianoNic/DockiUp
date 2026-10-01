using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DockiUp.Application.ImageUpdates;
using DockiUp.Application.Interfaces;
using DockiUp.Infrastructure.Services;
using Moq;

namespace DockiUp.Tests.Infrastructure;

/// <summary>The registry client against a scripted fake registry: token challenge, digests, tags, pagination, errors.</summary>
public class RegistryClientTests
{
    private const string Digest = "sha256:1111111111111111111111111111111111111111111111111111111111111111";

    /// <summary>Answers each request with the first route whose predicate matches; records every request.</summary>
    private sealed class FakeRegistry : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

        public FakeRegistry On(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _routes.Add((match, respond));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var route = _routes.FirstOrDefault(r => r.Match(request));
            return Task.FromResult(route.Respond?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static bool Authed(HttpRequestMessage r) => r.Headers.Authorization?.Parameter == "tok";

    private static HttpResponseMessage Challenge(string realm = "https://auth.example.com/token", string service = "registry.example.com")
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer",
            $"realm=\"{realm}\",service=\"{service}\",scope=\"repository:library/nginx:pull\""));
        return response;
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage WithDigest(string digest)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        response.Headers.Add("Docker-Content-Digest", digest);
        return response;
    }

    private static RegistryClient Client(FakeRegistry fake, IRegistryCredentialsProvider? creds = null)
        => new(new HttpClient(fake), creds ?? new AnonymousRegistryCredentials());

    private static ImageReference Ref(string s) => ImageReference.TryParse(s, out var r) ? r : throw new ArgumentException(s);

    [Fact]
    public async Task GetDigest_DockerHub_FollowsBearerChallenge_FetchesToken_AndReadsDigestHeaderFromHead()
    {
        var fake = new FakeRegistry()
            .On(r => r.RequestUri!.Host == "auth.docker.io", _ => Json("""{"token":"tok"}"""))
            .On(r => r.Method == HttpMethod.Head && Authed(r), _ => WithDigest(Digest))
            .On(r => !Authed(r), _ => Challenge("https://auth.docker.io/token", "registry.docker.io"));

        var digest = await Client(fake).GetDigestAsync(Ref("nginx"));

        Assert.Equal(Digest, digest);
        Assert.Equal("https://registry-1.docker.io/v2/library/nginx/manifests/latest", fake.Requests[0].RequestUri!.ToString());
        var token = fake.Requests.Single(r => r.RequestUri!.Host == "auth.docker.io").RequestUri!.ToString();
        Assert.Contains("service=registry.docker.io", token);
        Assert.Contains("scope=repository%3Alibrary%2Fnginx%3Apull", token);
        Assert.Null(fake.Requests.Single(r => r.RequestUri!.Host == "auth.docker.io").Headers.Authorization); // anonymous
        // Asks for index types so a multi-arch tag answers with the digest docker pull records.
        var accept = fake.Requests.Last().Headers.Accept.Select(a => a.MediaType).ToList();
        Assert.Equal("application/vnd.oci.image.index.v1+json", accept[0]);
        Assert.Contains("application/vnd.docker.distribution.manifest.list.v2+json", accept);
    }

    [Fact]
    public async Task GetDigest_Ghcr_AcceptsAccessTokenField_AndUsesTheRegistryHost()
    {
        var fake = new FakeRegistry()
            .On(r => r.RequestUri!.AbsolutePath == "/token", _ => Json("""{"access_token":"tok"}"""))
            .On(r => Authed(r), _ => WithDigest(Digest))
            .On(_ => true, _ => Challenge("https://ghcr.io/token", "ghcr.io"));

        Assert.Equal(Digest, await Client(fake).GetDigestAsync(Ref("ghcr.io/owner/app:v1")));
        Assert.Equal("https://ghcr.io/v2/owner/app/manifests/v1", fake.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetDigest_WithoutDigestHeader_FallsBackToGet_AndHashesTheManifest()
    {
        var manifest = """{"schemaVersion":2}""";
        var fake = new FakeRegistry()
            .On(r => r.Method == HttpMethod.Head, _ => new HttpResponseMessage(HttpStatusCode.OK))
            .On(r => r.Method == HttpMethod.Get, _ => Json(manifest));

        var digest = await Client(fake).GetDigestAsync(Ref("registry.example.com/team/app:1.0"));

        var expected = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
        Assert.Equal(expected, digest);
    }

    [Fact]
    public async Task GetDigest_UnknownTag_ReturnsNull()
    {
        var fake = new FakeRegistry().On(_ => true, _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await Client(fake).GetDigestAsync(Ref("ghcr.io/owner/app:nope")));
    }

    [Fact]
    public async Task GetDigest_PrivateImage_ThrowsReadableError()
    {
        // Token endpoint hands out a token, but the registry still refuses it (no access to a private repo).
        var fake = new FakeRegistry()
            .On(r => r.RequestUri!.Host == "auth.example.com", _ => Json("""{"token":"tok"}"""))
            .On(_ => true, _ => Challenge());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Client(fake).GetDigestAsync(Ref("registry.example.com/private/app")));
        Assert.Contains("access denied", ex.Message);
    }

    [Fact]
    public async Task GetDigest_RateLimited_ThrowsReadableError()
    {
        var fake = new FakeRegistry().On(_ => true, _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Client(fake).GetDigestAsync(Ref("nginx:1.27")));
        Assert.Contains("rate limited", ex.Message);
    }

    [Fact]
    public async Task GetDigest_TokenEndpointFails_Throws()
    {
        var fake = new FakeRegistry()
            .On(r => r.RequestUri!.Host == "auth.example.com", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError))
            .On(_ => true, _ => Challenge());
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Client(fake).GetDigestAsync(Ref("registry.example.com/app")));
        Assert.Contains("pull token", ex.Message);
    }

    [Fact]
    public async Task Token_WithCredentials_SendsBasicAuthToTheRealm()
    {
        var creds = new Mock<IRegistryCredentialsProvider>();
        creds.Setup(c => c.GetAsync("registry.example.com", Moq.It.IsAny<CancellationToken>())).ReturnsAsync(new RegistryCredentials("u", "p"));
        var fake = new FakeRegistry()
            .On(r => r.RequestUri!.Host == "auth.example.com", _ => Json("""{"token":"tok"}"""))
            .On(Authed, _ => WithDigest(Digest))
            .On(_ => true, _ => Challenge());

        await Client(fake, creds.Object).GetDigestAsync(Ref("registry.example.com/app"));

        var auth = fake.Requests.Single(r => r.RequestUri!.Host == "auth.example.com").Headers.Authorization!;
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("u:p")), auth.Parameter);
    }

    [Fact]
    public async Task ListTags_FollowsLinkPagination_ReusesTheToken()
    {
        var fake = new FakeRegistry()
            .On(r => r.RequestUri!.Host == "auth.example.com", _ => Json("""{"token":"tok"}"""))
            .On(r => Authed(r) && r.RequestUri!.Query.Contains("last=b"), _ => Json("""{"name":"app","tags":["c"]}"""))
            .On(r => Authed(r), _ =>
            {
                var page = Json("""{"name":"app","tags":["a","b"]}""");
                page.Headers.Add("Link", "</v2/team/app/tags/list?last=b&n=1000>; rel=\"next\"");
                return page;
            })
            .On(_ => true, _ => Challenge());

        var tags = await Client(fake).ListTagsAsync(Ref("registry.example.com/team/app"));

        Assert.Equal(["a", "b", "c"], tags);
        Assert.Single(fake.Requests, r => r.RequestUri!.Host == "auth.example.com");
        Assert.Equal("https://registry.example.com/v2/team/app/tags/list?last=b&n=1000", fake.Requests.Last().RequestUri!.ToString());
    }

    [Fact]
    public async Task ListTags_CapsTheCount_AndStopsPaging()
    {
        var fake = new FakeRegistry().On(_ => true, _ =>
        {
            var page = Json("""{"tags":["a","b","c"]}""");
            page.Headers.Add("Link", "</v2/app/tags/list?last=c&n=1000>; rel=\"next\"");
            return page;
        });

        var tags = await Client(fake).ListTagsAsync(Ref("registry.example.com/app"), max: 2);

        Assert.Equal(["a", "b"], tags);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task ListTags_IgnoresLinksToOtherHosts()
    {
        var fake = new FakeRegistry().On(_ => true, _ =>
        {
            var page = Json("""{"tags":["a"]}""");
            page.Headers.Add("Link", "<https://evil.example.org/v2/app/tags/list?last=a>; rel=\"next\"");
            return page;
        });

        Assert.Equal(["a"], await Client(fake).ListTagsAsync(Ref("registry.example.com/app")));
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task ListTags_UnknownRepository_ThrowsNotFound()
    {
        var fake = new FakeRegistry().On(_ => true, _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Client(fake).ListTagsAsync(Ref("registry.example.com/missing")));
    }

    [Fact]
    public async Task LocalRegistry_UsesPlainHttp()
    {
        var fake = new FakeRegistry().On(_ => true, _ => WithDigest(Digest));
        await Client(fake).GetDigestAsync(Ref("localhost:5000/app:dev"));
        Assert.Equal("http://localhost:5000/v2/app/manifests/dev", fake.Requests[0].RequestUri!.ToString());
    }
}
