using DockiUp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Services
{
    public class WebhookSecretService : IWebhookSecretService
    {
        private readonly DockiUpDbContext _dbContext;

        public WebhookSecretService(DockiUpDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<string> GetSecretByIdentifierAsync(string identifier)
        {
            // Query the database for the secret associated with the identifier
            var secret = await _dbContext.WebhookSecrets
                                          .Where(w => w.Identifier == identifier)
                                          .Select(w => w.Secret)
                                          .FirstOrDefaultAsync();

            return secret;
        }
    }
}
