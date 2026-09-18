namespace DockiUp.Application.Dtos
{
    public class AppInfoDto
    {
        public required string Version { get; set; }
        public required string Environment { get; set; }

        // OIDC/OAuth2 config surfaced to the SPA so it can configure angular-auth-oidc-client at runtime.
        // All empty when auth is disabled; AuthEnabled lets the SPA skip the whole login flow (open mode).
        public string Authority { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string PostLogoutRedirectUri { get; set; } = string.Empty;
        public string Scope { get; set; } = "openid profile email";
        public bool AuthEnabled { get; set; }
    }
}
