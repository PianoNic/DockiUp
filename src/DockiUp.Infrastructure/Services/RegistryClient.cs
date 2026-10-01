using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DockiUp.Application.ImageUpdates;
using DockiUp.Application.Interfaces;

namespace DockiUp.Infrastructure.Services
{
    /// <summary>Docker Registry HTTP API v2 / OCI distribution client (typed HttpClient). Handles the
    /// token flow: an unauthenticated request gets 401 + a `WWW-Authenticate: Bearer realm=...` challenge,
    /// we fetch a pull-scoped token from the realm (anonymously, or with credentials when the provider has
    /// some) and retry with it.</summary>
    public sealed class RegistryClient(HttpClient http, IRegistryCredentialsProvider credentials) : IRegistryClient
    {
        // Index types first: for a multi-arch tag the registry then answers with the index digest, which is
        // what `docker pull` stores in RepoDigests. Single-arch images answer with their manifest digest.
        private static readonly string[] ManifestTypes =
        [
            "application/vnd.oci.image.index.v1+json",
            "application/vnd.docker.distribution.manifest.list.v2+json",
            "application/vnd.docker.distribution.manifest.v2+json",
            "application/vnd.oci.image.manifest.v1+json",
        ];

        private const int PageSize = 1000;

        public async Task<string?> GetDigestAsync(ImageReference image, CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl(image)}/v2/{image.Repository}/manifests/{Uri.EscapeDataString(image.Tag)}";
            var session = new Session(image);

            // HEAD is enough when the registry returns Docker-Content-Digest (and doesn't count towards Docker
            // Hub's pull limit); otherwise GET the manifest and hash the exact bytes, which is the digest.
            using (var head = await SendAsync(session, () => ManifestRequest(HttpMethod.Head, url), cancellationToken))
            {
                if (head.StatusCode == HttpStatusCode.NotFound) return null;
                EnsureSuccess(head, image);
                if (DigestHeader(head) is { } digest) return digest;
            }

            using var get = await SendAsync(session, () => ManifestRequest(HttpMethod.Get, url), cancellationToken);
            if (get.StatusCode == HttpStatusCode.NotFound) return null;
            EnsureSuccess(get, image);
            if (DigestHeader(get) is { } fromGet) return fromGet;
            var body = await get.Content.ReadAsByteArrayAsync(cancellationToken);
            return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(body));
        }

        public async Task<IReadOnlyList<string>> ListTagsAsync(ImageReference image, int max = 5000, CancellationToken cancellationToken = default)
        {
            var baseUrl = BaseUrl(image);
            var session = new Session(image);
            var tags = new List<string>();
            string? next = $"{baseUrl}/v2/{image.Repository}/tags/list?n={PageSize}";

            while (next is not null && tags.Count < max)
            {
                var url = next;
                using var response = await SendAsync(session, () => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new KeyNotFoundException($"{image.Name} was not found in its registry.");
                EnsureSuccess(response, image);

                using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                if (json.RootElement.TryGetProperty("tags", out var list) && list.ValueKind == JsonValueKind.Array)
                    tags.AddRange(list.EnumerateArray().Select(t => t.GetString()).OfType<string>());

                next = NextPage(response, baseUrl, url);
            }
            return tags.Count > max ? tags.Take(max).ToList() : tags;
        }

        private static string BaseUrl(ImageReference image)
        {
            // Plain HTTP only for a registry on this machine (the usual `registry:2` dev setup).
            var host = image.ApiHost;
            var local = host.StartsWith("localhost", StringComparison.Ordinal) || host.StartsWith("127.0.0.1", StringComparison.Ordinal);
            return (local ? "http://" : "https://") + host;
        }

        private static HttpRequestMessage ManifestRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            foreach (var type in ManifestTypes) request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(type));
            return request;
        }

        private static string? DigestHeader(HttpResponseMessage response)
            => response.Headers.TryGetValues("Docker-Content-Digest", out var values) ? values.FirstOrDefault() : null;

        /// <summary>The `Link: &lt;/v2/...?last=x&amp;n=y&gt;; rel="next"` header, resolved against the request URL.</summary>
        private static string? NextPage(HttpResponseMessage response, string baseUrl, string current)
        {
            if (!response.Headers.TryGetValues("Link", out var links)) return null;
            foreach (var link in links.SelectMany(l => l.Split(',')))
            {
                if (!link.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase)) continue;
                var start = link.IndexOf('<');
                var end = link.IndexOf('>');
                if (start < 0 || end <= start) continue;
                var target = new Uri(new Uri(current), link[(start + 1)..end]);
                // Only follow pages on the same registry: the header is registry-controlled.
                return target.GetLeftPart(UriPartial.Authority) == new Uri(baseUrl).GetLeftPart(UriPartial.Authority) ? target.ToString() : null;
            }
            return null;
        }

        private static void EnsureSuccess(HttpResponseMessage response, ImageReference image)
        {
            if (response.IsSuccessStatusCode) return;
            var reason = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "access denied (private image? registry credentials are not supported yet)",
                HttpStatusCode.TooManyRequests => "rate limited by the registry, try again later",
                _ => $"registry answered {(int)response.StatusCode} {response.ReasonPhrase}",
            };
            throw new HttpRequestException($"{image.Name}: {reason}", null, response.StatusCode);
        }

        // Sends a request; on a Bearer challenge fetches a token once per session and retries with it.
        private async Task<HttpResponseMessage> SendAsync(Session session, Func<HttpRequestMessage> build, CancellationToken cancellationToken)
        {
            if (session.Authorization is null)
            {
                var first = build();
                var response = await http.SendAsync(first, cancellationToken);
                if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

                session.Authorization = await AuthorizeAsync(session.Image, response.Headers.WwwAuthenticate, cancellationToken);
                response.Dispose();
                if (session.Authorization is null)
                    throw new HttpRequestException($"{session.Image.Name}: access denied (private image? registry credentials are not supported yet)", null, HttpStatusCode.Unauthorized);
            }
            var request = build();
            request.Headers.Authorization = session.Authorization;
            return await http.SendAsync(request, cancellationToken);
        }

        private async Task<AuthenticationHeaderValue?> AuthorizeAsync(ImageReference image, HttpHeaderValueCollection<AuthenticationHeaderValue> challenges, CancellationToken cancellationToken)
        {
            var creds = await credentials.GetAsync(image.Registry, cancellationToken);
            var basic = creds is null ? null
                : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{creds.Username}:{creds.Password}")));

            foreach (var challenge in challenges)
            {
                if (challenge.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase)) return basic;
                if (!challenge.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)) continue;

                var parameters = ParseChallenge(challenge.Parameter);
                if (!parameters.TryGetValue("realm", out var realm) || !Uri.TryCreate(realm, UriKind.Absolute, out var realmUri)
                    || realmUri.Scheme is not ("https" or "http"))
                    continue;

                var query = new List<string>();
                if (parameters.TryGetValue("service", out var service)) query.Add("service=" + Uri.EscapeDataString(service));
                query.Add("scope=" + Uri.EscapeDataString(parameters.TryGetValue("scope", out var scope) ? scope : $"repository:{image.Repository}:pull"));
                var tokenUrl = realm + (realm.Contains('?') ? "&" : "?") + string.Join('&', query);

                using var request = new HttpRequestMessage(HttpMethod.Get, tokenUrl);
                // ponytail: credentials go to whatever realm the registry names; restrict to the registry's own
                // domain once real credentials are plugged in.
                if (basic is not null) request.Headers.Authorization = basic;
                using var response = await http.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"{image.Name}: the registry refused a pull token ({(int)response.StatusCode})", null, response.StatusCode);

                using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                var token = json.RootElement.TryGetProperty("token", out var t) ? t.GetString()
                    : json.RootElement.TryGetProperty("access_token", out var a) ? a.GetString() : null;
                return string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);
            }
            return null;
        }

        /// <summary>`realm="https://auth.docker.io/token",service="registry.docker.io",scope="repository:x:pull"`.</summary>
        private static Dictionary<string, string> ParseChallenge(string? parameter)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(parameter)) return result;
            var i = 0;
            while (i < parameter.Length)
            {
                while (i < parameter.Length && (parameter[i] == ',' || char.IsWhiteSpace(parameter[i]))) i++;
                var eq = parameter.IndexOf('=', i);
                if (eq < 0) break;
                var key = parameter[i..eq].Trim();
                i = eq + 1;
                string value;
                if (i < parameter.Length && parameter[i] == '"')
                {
                    var close = parameter.IndexOf('"', i + 1);
                    if (close < 0) close = parameter.Length;
                    value = parameter[(i + 1)..close];
                    i = close + 1;
                }
                else
                {
                    var comma = parameter.IndexOf(',', i);
                    if (comma < 0) comma = parameter.Length;
                    value = parameter[i..comma].Trim();
                    i = comma;
                }
                result[key] = value;
            }
            return result;
        }

        private sealed class Session(ImageReference image)
        {
            public ImageReference Image { get; } = image;
            public AuthenticationHeaderValue? Authorization { get; set; }
        }
    }

    /// <summary>Default: no stored registry credentials, every request is anonymous (public images).</summary>
    public sealed class AnonymousRegistryCredentials : IRegistryCredentialsProvider
    {
        public Task<RegistryCredentials?> GetAsync(string registry, CancellationToken cancellationToken = default)
            => Task.FromResult<RegistryCredentials?>(null);
    }
}
