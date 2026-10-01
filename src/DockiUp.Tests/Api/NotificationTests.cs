using System.Security.Cryptography;
using System.Text.Json;
using DockiUp.API.Controllers;
using DockiUp.API.Notifications;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Notifications;
using DockiUp.Domain;
using DockiUp.Infrastructure;
using DockiUp.Infrastructure.Services;
using DockiUp.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DockiUp.Tests.Api;

public static class TestVault
{
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Vault:MasterKey"] = Key }).Build();

    public static SecretsVaultService Create(IDockiUpDbContext db) => new(db, Config());
}

public class NotificationEventHandlerTests
{
    private readonly List<NotificationMessage> _queued = [];
    private readonly NotificationEventHandler _handler;

    public NotificationEventHandlerTests()
    {
        var dispatcher = new Mock<INotificationDispatcher>();
        dispatcher.Setup(d => d.Enqueue(It.IsAny<NotificationMessage>())).Callback<NotificationMessage>(_queued.Add);
        _handler = new NotificationEventHandler(dispatcher.Object);
    }

    [Fact]
    public async Task DeploymentFinished_MapsToSucceededOrFailed()
    {
        await _handler.Handle(new DeploymentFinished(Guid.NewGuid(), Guid.NewGuid(), "App", true, "Manual", "abcdef123", null), default);
        await _handler.Handle(new DeploymentFinished(Guid.NewGuid(), Guid.NewGuid(), "App", false, "Webhook", null, "port taken"), default);

        Assert.Equal(NotificationEvent.DeploymentSucceeded, _queued[0].Event);
        Assert.Contains("abcdef1", _queued[0].Body);
        Assert.Equal(NotificationEvent.DeploymentFailed, _queued[1].Event);
        Assert.Contains("port taken", _queued[1].Body);
        Assert.Equal("App", _queued[1].Data!["projectName"]);
    }

    [Fact]
    public async Task NodeImageAndCleanupEvents_Map()
    {
        await _handler.Handle(new NodeConnectionChanged(Guid.NewGuid(), "edge", false), default);
        await _handler.Handle(new NodeConnectionChanged(Guid.NewGuid(), "edge", true), default);
        await _handler.Handle(new DockiUp.Application.ImageUpdates.ImageUpdatesFound(Guid.NewGuid(), "App", DockiUp.Domain.Enums.ImageUpdatePolicy.Notify,
            [new DockiUp.Application.ImageUpdates.ImageUpdateDto(Guid.NewGuid(), "web", "nginx:latest", "sha256:a", "sha256:b", DateTime.UtcNow, true, null)]), default);
        await _handler.Handle(new CleanupCompleted("Removed 3 images", 1024, "edge"), default);

        Assert.Equal([NotificationEvent.NodeOffline, NotificationEvent.NodeOnline, NotificationEvent.ImageUpdateAvailable, NotificationEvent.CleanupReport],
            _queued.Select(m => m.Event));
        Assert.Contains("nginx:latest", _queued[2].Body);
        Assert.Equal("Cleanup finished on edge", _queued[3].Title);
    }
}

public class NotificationDispatcherTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Mock<INotificationSender> _sender = new();
    private readonly List<(string Name, ChannelTarget Target)> _sent = [];

    private IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDockiUpDbContext>(_ => TestDb.Create(_dbName));
        services.AddScoped<ISecretsVaultService>(sp => TestVault.Create(sp.GetRequiredService<IDockiUpDbContext>()));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private void Channel(string name, NotificationEvent events, bool enabled = true, string? secret = "https://hooks.slack.com/x")
    {
        using var db = TestDb.Create(_dbName);
        db.NotificationChannels.Add(new NotificationChannel
        {
            Name = name, Type = NotificationChannelType.Slack, Enabled = enabled, Events = events,
            SecretEncrypted = secret is null ? null : ((ISecretsVaultService)TestVault.Create(db)).Encrypt(secret),
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task SendsOnlyToEnabledChannelsSubscribedToTheEvent_WithDecryptedSecret()
    {
        Channel("failures", NotificationEvent.DeploymentFailed | NotificationEvent.NodeOffline);
        Channel("successes", NotificationEvent.DeploymentSucceeded);
        Channel("disabled", NotificationEvent.DeploymentFailed, enabled: false);
        _sender.Setup(s => s.SendAsync(It.IsAny<ChannelTarget>(), It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()))
            .Callback((ChannelTarget t, NotificationMessage _, CancellationToken _) => _sent.Add(("", t)))
            .Returns(Task.CompletedTask);
        var dispatcher = new NotificationDispatcher(Scopes(), _sender.Object, NullLogger<NotificationDispatcher>.Instance);

        await dispatcher.DispatchAsync(new NotificationMessage(NotificationEvent.DeploymentFailed, "t", "b"), CancellationToken.None);

        var (_, target) = Assert.Single(_sent);
        Assert.Equal("https://hooks.slack.com/x", target.Secret);
    }

    [Fact]
    public async Task AFailingChannel_IsLoggedNotThrown_AndOthersStillGetIt()
    {
        Channel("a", NotificationEvent.NodeOnline);
        Channel("b", NotificationEvent.NodeOnline);
        var calls = 0;
        _sender.Setup(s => s.SendAsync(It.IsAny<ChannelTarget>(), It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1 ? Task.FromException(new InvalidOperationException("Slack answered 500")) : Task.CompletedTask);
        var logger = new Mock<ILogger<NotificationDispatcher>>();
        var dispatcher = new NotificationDispatcher(Scopes(), _sender.Object, logger.Object);

        await dispatcher.DispatchAsync(new NotificationMessage(NotificationEvent.NodeOnline, "t", "b"), CancellationToken.None);

        Assert.Equal(2, calls);
        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Slack answered 500")),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}

public class NotificationsControllerTests
{
    private readonly DockiUpDbContext _db = TestDb.Create();
    private readonly Mock<INotificationSender> _sender = new();
    private NotificationsController Controller() => new(_db, TestVault.Create(_db), _sender.Object);

    private static SaveNotificationChannelRequest Discord(string? secret = "https://discord.com/api/webhooks/1/abcdefghijklmnop") =>
        new("Ops", NotificationChannelType.Discord, true, [NotificationEvent.DeploymentFailed, NotificationEvent.NodeOffline], null, null, null, secret);

    private static T Value<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;

    [Fact]
    public async Task Create_StoresSecretEncrypted_AndReturnsItMasked()
    {
        var dto = Value(await Controller().CreateNotificationChannel(Discord(), default));

        Assert.Equal("••••••••mnop", dto.SecretMasked);
        Assert.Equal([NotificationEvent.DeploymentFailed, NotificationEvent.NodeOffline], dto.Events);
        Assert.DoesNotContain("abcdefghijklmnop", JsonSerializer.Serialize(dto));
        var row = await _db.NotificationChannels.SingleAsync();
        Assert.DoesNotContain("discord.com", row.SecretEncrypted);
        Assert.Equal(NotificationEvent.DeploymentFailed | NotificationEvent.NodeOffline, row.Events);

        var listed = Value(await Controller().ListNotificationChannels(default));
        Assert.DoesNotContain("abcdefghijklmnop", JsonSerializer.Serialize(listed));
    }

    [Fact]
    public async Task Update_NullSecretKeepsIt_EmptyClearsIt()
    {
        var created = Value(await Controller().CreateNotificationChannel(
            new SaveNotificationChannelRequest("n", NotificationChannelType.Ntfy, true, [], null, "topic", null, "tk_abcdefghijkl"), default));

        var kept = Value(await Controller().UpdateNotificationChannel(created.Id,
            new SaveNotificationChannelRequest("n2", NotificationChannelType.Ntfy, false, [NotificationEvent.CleanupReport], "https://ntfy.example.com", "topic", null, null), default));
        Assert.Equal("••••••••ijkl", kept.SecretMasked);
        Assert.Equal("n2", kept.Name);
        Assert.False(kept.Enabled);

        var cleared = Value(await Controller().UpdateNotificationChannel(created.Id,
            new SaveNotificationChannelRequest("n2", NotificationChannelType.Ntfy, true, [], null, "topic", null, ""), default));
        Assert.Null(cleared.SecretMasked);
    }

    [Theory]
    [InlineData(NotificationChannelType.Discord, null, null, "not a url")]
    [InlineData(NotificationChannelType.Slack, null, null, null)]
    [InlineData(NotificationChannelType.Telegram, null, null, "123:abc")]
    [InlineData(NotificationChannelType.Telegram, null, "42", null)]
    [InlineData(NotificationChannelType.Ntfy, null, null, null)]
    [InlineData(NotificationChannelType.Ntfy, "ftp://x", "t", null)]
    [InlineData(NotificationChannelType.Webhook, null, null, null)]
    public async Task Create_RejectsMissingOrMalformedFields(NotificationChannelType type, string? url, string? target, string? secret)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Controller().CreateNotificationChannel(
            new SaveNotificationChannelRequest("x", type, true, [], url, target, null, secret), default));
    }

    [Fact]
    public async Task Create_RejectsBadWebhookHeaderName()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Controller().CreateNotificationChannel(
            new SaveNotificationChannelRequest("x", NotificationChannelType.Webhook, true, [], "https://h.example.com", null, "Bad Header:", "v"), default));
    }

    [Fact]
    public async Task Test_SendsDecryptedChannel_AndReportsFailureAs400()
    {
        var created = Value(await Controller().CreateNotificationChannel(Discord(), default));
        ChannelTarget? target = null;
        _sender.Setup(s => s.SendAsync(It.IsAny<ChannelTarget>(), It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()))
            .Callback((ChannelTarget t, NotificationMessage _, CancellationToken _) => target = t).Returns(Task.CompletedTask);

        Assert.IsType<NoContentResult>(await Controller().TestNotificationChannel(created.Id, default));
        Assert.Equal("https://discord.com/api/webhooks/1/abcdefghijklmnop", target!.Secret);

        _sender.Setup(s => s.SendAsync(It.IsAny<ChannelTarget>(), It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Discord answered 404"));
        var bad = Assert.IsType<BadRequestObjectResult>(await Controller().TestNotificationChannel(created.Id, default));
        Assert.Contains("Discord answered 404", ((ProblemDetails)bad.Value!).Detail);
    }

    [Fact]
    public async Task Delete_And_UnknownIds()
    {
        var created = Value(await Controller().CreateNotificationChannel(Discord(), default));
        Assert.IsType<NoContentResult>(await Controller().DeleteNotificationChannel(created.Id, default));
        Assert.Empty(_db.NotificationChannels);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Controller().DeleteNotificationChannel(created.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Controller().TestNotificationChannel(created.Id, default));
    }
}
