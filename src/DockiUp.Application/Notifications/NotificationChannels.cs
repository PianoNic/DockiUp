using DockiUp.Application.Interfaces;
using DockiUp.Domain;

namespace DockiUp.Application.Notifications
{
    /// <summary>A channel as the API shows it: the credential only ever masked.</summary>
    public record NotificationChannelDto(Guid Id, string Name, NotificationChannelType Type, bool Enabled, NotificationEvent[] Events,
        string? Url, string? Target, string? HeaderName, string? SecretMasked, DateTime CreatedAt);

    /// <summary>Create/update a channel. <see cref="Secret"/>: null keeps the stored one, "" removes it, anything
    /// else replaces it. Discord/Slack: the webhook URL; Telegram: the bot token; ntfy: an optional access token;
    /// Webhook: an optional value for <see cref="HeaderName"/> (default X-Webhook-Secret).</summary>
    public record SaveNotificationChannelRequest(string Name, NotificationChannelType Type, bool Enabled, NotificationEvent[] Events,
        string? Url, string? Target, string? HeaderName, string? Secret);

    public static class NotificationChannels
    {
        public static NotificationEvent[] AllEvents { get; } =
            Enum.GetValues<NotificationEvent>().Where(e => e != NotificationEvent.None).ToArray();

        public static NotificationEvent[] Split(NotificationEvent flags) => AllEvents.Where(e => flags.HasFlag(e)).ToArray();

        public static NotificationChannelDto ToDto(NotificationChannel c, ISecretsVaultService vault) => new(
            c.Id, c.Name, c.Type, c.Enabled, Split(c.Events), c.Url, c.Target, c.HeaderName,
            c.SecretEncrypted is null ? null : SecretMask.Mask(vault.Decrypt(c.SecretEncrypted)), c.CreatedAt);

        public static ChannelTarget ToTarget(NotificationChannel c, ISecretsVaultService vault)
            => new(c.Type, c.Url, c.Target, c.HeaderName, c.SecretEncrypted is null ? null : vault.Decrypt(c.SecretEncrypted));

        /// <summary>Applies a request to a channel (new or existing). Throws <see cref="ArgumentException"/> with
        /// a message the UI can show when a field the channel type needs is missing or malformed.</summary>
        public static void Apply(NotificationChannel channel, SaveNotificationChannelRequest request, ISecretsVaultService vault)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("A name is required.");
            if (!Enum.IsDefined(request.Type)) throw new ArgumentException("Unknown channel type.");

            var secret = request.Secret switch
            {
                null => channel.SecretEncrypted is null ? null : vault.Decrypt(channel.SecretEncrypted),
                "" => null,
                var s => s.Trim(),
            };
            var url = Clean(request.Url);
            var target = Clean(request.Target);

            switch (request.Type)
            {
                case NotificationChannelType.Discord:
                case NotificationChannelType.Slack:
                    if (!IsHttpUrl(secret)) throw new ArgumentException($"{request.Type} needs its incoming webhook URL.");
                    url = target = null;
                    break;
                case NotificationChannelType.Telegram:
                    if (string.IsNullOrEmpty(secret)) throw new ArgumentException("Telegram needs the bot token.");
                    if (target is null) throw new ArgumentException("Telegram needs the chat id.");
                    url = null;
                    break;
                case NotificationChannelType.Ntfy:
                    if (target is null) throw new ArgumentException("ntfy needs a topic.");
                    if (url is not null && !IsHttpUrl(url)) throw new ArgumentException("The ntfy server must be an http(s) URL.");
                    break;
                case NotificationChannelType.Webhook:
                    if (!IsHttpUrl(url)) throw new ArgumentException("The webhook needs an http(s) URL.");
                    if (Clean(request.HeaderName) is { } header && !header.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
                        throw new ArgumentException("The header name may only contain letters, digits and dashes.");
                    target = null;
                    break;
            }

            channel.Name = request.Name.Trim();
            channel.Type = request.Type;
            channel.Enabled = request.Enabled;
            channel.Events = (request.Events ?? []).Aggregate(NotificationEvent.None, (all, e) => all | e);
            channel.Url = url;
            channel.Target = target;
            channel.HeaderName = request.Type == NotificationChannelType.Webhook ? Clean(request.HeaderName) : null;
            channel.SecretEncrypted = secret is null ? null : vault.Encrypt(secret);
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static bool IsHttpUrl(string? value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    /// <summary>Shows that a credential is set, and which one, without revealing it.</summary>
    public static class SecretMask
    {
        public static string Mask(string secret) => secret.Length >= 12 ? "••••••••" + secret[^4..] : "••••••••";
    }
}
