using MediatR;
using Microsoft.Extensions.Logging;

namespace DockiUp.Application.Queries
{
    public class IsValidGitRepositoryQuery : IRequest<bool>
    {
        public string Url { get; set; }
        public IsValidGitRepositoryQuery(string url)
        {
            Url = url;
        }
    }

    public class IsValidGitRepositoryQueryHandler : IRequestHandler<IsValidGitRepositoryQuery, bool>
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<IsValidGitRepositoryQueryHandler> _logger;

        public IsValidGitRepositoryQueryHandler(HttpClient httpClient, ILogger<IsValidGitRepositoryQueryHandler> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<bool> Handle(IsValidGitRepositoryQuery request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
            {
                _logger.LogError("URL cannot be null or empty.");
                return false;
            }

            var url = request.Url.EndsWith(".git") ? request.Url : request.Url + ".git";
            var gitServiceUrl = $"{url}/info/refs?service=git-upload-pack";

            try
            {
                var response = await _httpClient.GetAsync(gitServiceUrl, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode && content.Contains("# service=git-upload-pack"))
                {
                    _logger.LogInformation("Valid Git repository.");
                    return true;
                }
                else
                {
                    _logger.LogWarning("URL is reachable but doesn't appear to be a Git repository.");
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Error checking Git link: {ex.Message}");
                return false;
            }
        }
    }
}
