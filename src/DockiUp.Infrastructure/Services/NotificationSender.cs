using System.Net.Http.Json;
using DockiUp.Application.Notifications;
using DockiUp.Domain;

namespace DockiUp.Infrastructure.Services
{
    /// <summary>Posts a message to Discord, Telegram, ntfy, Slack or a generic webhook. Uses its own HttpClient
    /// on purpose: IHttpClientFactory's logging handlers log request URLs, and Discord/Slack webhook URLs
    /// and Telegram's bot URL carry the credential.</summary>
    public sealed class NotificationSender(HttpClient http) : INotificationSender
    {
        public async Task SendAsync(ChannelTarget channel, NotificationMessage message, CancellationToken cancellationToken)
        {
            using var request = Build(channel, message);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The body explains the rejection (bad chat id, unknown webhook); the URL is deliberately left out.
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (body.Length > 300) body = body[..300];
                throw new InvalidOperationException($"{channel.Type} answered {(int)response.StatusCode} {response.ReasonPhrase}: {body}".TrimEnd(' ', ':'));
            }
        }

        public static HttpRequestMessage Build(ChannelTarget channel, NotificationMessage message)
        {
            // A failed deploy carries compose's output; stay well inside every service's message limit.
            if (message.Body.Length > MaxBody) message = message with { Body = message.Body[..MaxBody] + "…" };
            switch (channel.Type)
            {
                case NotificationChannelType.Discord:
                    return Post(Required(channel.Secret, "webhook URL"), new
                    {
                        username = "DockiUp",
                        embeds = new[] { new { title = message.Title, description = message.Body, color = Color(message.Event) } },
                    });

                case NotificationChannelType.Slack:
                    return Post(Required(channel.Secret, "webhook URL"), new { text = $"*{message.Title}*\n{message.Body}" });

                case NotificationChannelType.Telegram:
                    return Post($"https://api.telegram.org/bot{Required(channel.Secret, "bot token")}/sendMessage", new
                    {
                        chat_id = Required(channel.Target, "chat id"),
                        text = $"{message.Title}\n\n{message.Body}",
                        disable_web_page_preview = true,
                    });

                case NotificationChannelType.Ntfy:
                {
                    // JSON publishing (POST to the server root) keeps non-ASCII titles intact, unlike headers.
                    var server = string.IsNullOrWhiteSpace(channel.Url) ? "https://ntfy.sh" : channel.Url.TrimEnd('/');
                    var request = Post(server, new
                    {
                        topic = Required(channel.Target, "topic"),
                        title = message.Title,
                        message = message.Body,
                        priority = IsBad(message.Event) ? 4 : 3,
                        tags = new[] { IsBad(message.Event) ? "warning" : "whale" },
                    });
                    if (!string.IsNullOrEmpty(channel.Secret))
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", channel.Secret);
                    return request;
                }

                case NotificationChannelType.Webhook:
                {
                    var request = Post(Required(channel.Url, "URL"), new
                    {
                        @event = message.Event == NotificationEvent.None ? "Test" : message.Event.ToString(),
                        title = message.Title,
                        message = message.Body,
                        timestamp = DateTime.UtcNow,
                        data = message.Data ?? new Dictionary<string, string?>(),
                    });
                    if (!string.IsNullOrEmpty(channel.Secret))
                        request.Headers.TryAddWithoutValidation(string.IsNullOrWhiteSpace(channel.HeaderName) ? DefaultWebhookHeader : channel.HeaderName, channel.Secret);
                    return request;
                }

                default:
                    throw new ArgumentException($"Unknown channel type {channel.Type}.");
            }
        }

        public const string DefaultWebhookHeader = "X-Webhook-Secret";
        private const int MaxBody = 1800;

        private static HttpRequestMessage Post(string url, object payload)
            // Buffered, so it carries a Content-Length: simple receivers mis-read chunked bodies as empty.
            => new(HttpMethod.Post, url) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload, JsonOptions), System.Text.Encoding.UTF8, "application/json") };

        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

        private static string Required(string? value, string what)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"The channel has no {what}.") : value;

        private static bool IsBad(NotificationEvent e) => e is NotificationEvent.DeploymentFailed or NotificationEvent.NodeOffline;

        // Same palette as the UI's status colours: red for trouble, green for good news, blue for the rest.
        private static int Color(NotificationEvent e) => e switch
        {
            NotificationEvent.DeploymentFailed or NotificationEvent.NodeOffline => 0xE53935,
            NotificationEvent.DeploymentSucceeded or NotificationEvent.NodeOnline => 0x43A047,
            _ => 0x1E88E5,
        };
    }
}
