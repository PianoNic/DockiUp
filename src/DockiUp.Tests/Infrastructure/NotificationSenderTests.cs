using System.Net;
using System.Text.Json;
using DockiUp.Application.Notifications;
using DockiUp.Domain;
using DockiUp.Infrastructure.Services;

namespace DockiUp.Tests.Infrastructure;

/// <summary>Records every request instead of sending it, and answers with a fixed status.</summary>
public sealed class FakeHttpHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "") : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
        return new HttpResponseMessage(status) { Content = new StringContent(body), ReasonPhrase = status.ToString() };
    }
}

public class NotificationSenderTests
{
    private static readonly NotificationMessage Failed = new(NotificationEvent.DeploymentFailed, "Deployment of App failed", "boom",
        new Dictionary<string, string?> { ["projectName"] = "App" });

    private static async Task<(HttpRequestMessage Request, JsonElement Json)> Send(ChannelTarget channel, NotificationMessage? message = null)
    {
        var handler = new FakeHttpHandler();
        await new NotificationSender(new HttpClient(handler)).SendAsync(channel, message ?? Failed, CancellationToken.None);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        return (request, JsonDocument.Parse(body).RootElement);
    }

    [Fact]
    public async Task Discord_PostsEmbedToWebhookUrl_RedForFailures()
    {
        var (request, json) = await Send(new ChannelTarget(NotificationChannelType.Discord, null, null, null, "https://discord.com/api/webhooks/1/tok"));

        Assert.Equal("https://discord.com/api/webhooks/1/tok", request.RequestUri!.ToString());
        var embed = json.GetProperty("embeds")[0];
        Assert.Equal("Deployment of App failed", embed.GetProperty("title").GetString());
        Assert.Equal("boom", embed.GetProperty("description").GetString());
        Assert.Equal(0xE53935, embed.GetProperty("color").GetInt32());
    }

    [Fact]
    public async Task Slack_PostsBoldTitleAndBody()
    {
        var (request, json) = await Send(new ChannelTarget(NotificationChannelType.Slack, null, null, null, "https://hooks.slack.com/services/x"));

        Assert.Equal("https://hooks.slack.com/services/x", request.RequestUri!.ToString());
        Assert.Equal("*Deployment of App failed*\nboom", json.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Telegram_UsesBotTokenInPath_AndChatId()
    {
        var (request, json) = await Send(new ChannelTarget(NotificationChannelType.Telegram, null, "-100123", null, "123:ABC"));

        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", request.RequestUri!.ToString());
        Assert.Equal("-100123", json.GetProperty("chat_id").GetString());
        Assert.Equal("Deployment of App failed\n\nboom", json.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Ntfy_PostsJsonToServerRoot_WithBearerToken_AndHighPriorityForFailures()
    {
        var (request, json) = await Send(new ChannelTarget(NotificationChannelType.Ntfy, "https://ntfy.example.com/", "deploys", null, "tk_secret"));

        Assert.Equal("https://ntfy.example.com/", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("tk_secret", request.Headers.Authorization.Parameter);
        Assert.Equal("deploys", json.GetProperty("topic").GetString());
        Assert.Equal("Deployment of App failed", json.GetProperty("title").GetString());
        Assert.Equal(4, json.GetProperty("priority").GetInt32());
    }

    [Fact]
    public async Task Ntfy_DefaultsToNtfySh_AndSendsNoAuthWithoutToken()
    {
        var (request, _) = await Send(new ChannelTarget(NotificationChannelType.Ntfy, null, "deploys", null, null));

        Assert.Equal("https://ntfy.sh/", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Webhook_PostsStructuredPayload_WithSecretHeader()
    {
        var (request, json) = await Send(new ChannelTarget(NotificationChannelType.Webhook, "https://hook.example.com/in", null, "X-Token", "s3cret"));

        Assert.Equal("s3cret", request.Headers.GetValues("X-Token").Single());
        Assert.Equal("DeploymentFailed", json.GetProperty("event").GetString());
        Assert.Equal("boom", json.GetProperty("message").GetString());
        Assert.Equal("App", json.GetProperty("data").GetProperty("projectName").GetString());
    }

    [Fact]
    public async Task Webhook_DefaultHeaderName_AndTestEventName()
    {
        var (request, json) = await Send(new ChannelTarget(NotificationChannelType.Webhook, "https://hook.example.com/in", null, null, "s3cret"),
            new NotificationMessage(NotificationEvent.None, "t", "b"));

        Assert.Equal("s3cret", request.Headers.GetValues(NotificationSender.DefaultWebhookHeader).Single());
        Assert.Equal("Test", json.GetProperty("event").GetString());
    }

    [Fact]
    public async Task LongBodies_AreTruncated()
    {
        var (_, json) = await Send(new ChannelTarget(NotificationChannelType.Slack, null, null, null, "https://hooks.slack.com/x"),
            new NotificationMessage(NotificationEvent.DeploymentFailed, "t", new string('x', 5000)));

        Assert.True(json.GetProperty("text").GetString()!.Length < 2000);
    }

    [Fact]
    public async Task ErrorStatus_Throws_WithServiceAnswer_ButNeverTheCredentialUrl()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, "{\"message\":\"Unknown Webhook\"}");
        var sender = new NotificationSender(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new ChannelTarget(NotificationChannelType.Discord, null, null, null, "https://discord.com/api/webhooks/1/supersecrettoken"), Failed, CancellationToken.None));

        Assert.Contains("404", ex.Message);
        Assert.Contains("Unknown Webhook", ex.Message);
        Assert.DoesNotContain("supersecrettoken", ex.Message);
    }

    [Fact]
    public void MissingRequiredField_Throws()
    {
        Assert.Throws<ArgumentException>(() => NotificationSender.Build(new ChannelTarget(NotificationChannelType.Telegram, null, null, null, "123:ABC"), Failed));
    }

    [Fact]
    public void ChannelTarget_ToString_HidesSecret()
    {
        Assert.DoesNotContain("hidden", new ChannelTarget(NotificationChannelType.Telegram, null, "1", null, "hidden").ToString());
    }
}
