namespace DockiUp.Domain.Models
{
    public class WebhookSecret
    {
        public int Id { get; set; }
        public required string Identifier { get; set; }
        public required string Secret { get; set; }
    }
}
