namespace DockiUp.Application.Services
{
    public interface IWebhookSecretService
    {
        Task<string> GetSecretByIdentifierAsync(string identifier);
    }

}
