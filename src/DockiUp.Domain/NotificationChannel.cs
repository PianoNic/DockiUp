namespace DockiUp.Domain
{
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<NotificationChannelType>))]
    public enum NotificationChannelType
    {
        Discord = 0,
        Telegram = 1,
        Ntfy = 2,
        Slack = 3,
        Webhook = 4,
    }

    /// <summary>Things a channel can subscribe to. Flags, so a channel stores its whole selection in one column.</summary>
    [Flags]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<NotificationEvent>))]
    public enum NotificationEvent
    {
        None = 0,
        DeploymentSucceeded = 1,
        DeploymentFailed = 2,
        NodeOffline = 4,
        NodeOnline = 8,
        ImageUpdateAvailable = 16,
        CleanupReport = 32,
    }

    /// <summary>Where DockiUp sends notifications. The one credential a channel needs (Discord/Slack webhook
    /// URL, Telegram bot token, ntfy access token, webhook secret header value) is stored vault-encrypted in
    /// <see cref="SecretEncrypted"/>; the other fields are not sensitive.</summary>
    public class NotificationChannel : BaseEntity
    {
        public required string Name { get; set; }
        public required NotificationChannelType Type { get; set; }
        public bool Enabled { get; set; } = true;
        public NotificationEvent Events { get; set; }

        /// <summary>ntfy: server URL; Webhook: target URL.</summary>
        public string? Url { get; set; }
        /// <summary>Telegram: chat id; ntfy: topic.</summary>
        public string? Target { get; set; }
        /// <summary>Webhook: header that carries the secret.</summary>
        public string? HeaderName { get; set; }
        public string? SecretEncrypted { get; set; }
    }
}
