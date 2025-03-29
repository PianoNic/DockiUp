namespace DockiUp.Application.Dtos
{
    public class WebhookSecretDto
    {
        public int WebhookSecretDtoId { get; set; }
        public required string Identifier { get; set; }
        public required string Secret { get; set; }
    }
}
