using DockiUp.API.Notifications;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Notifications;
using DockiUp.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Controllers
{
    /// <summary>Notification channels (Discord, Telegram, ntfy, Slack, generic webhook) and the events each one
    /// receives. Channel credentials are stored vault-encrypted and only ever returned masked.</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationsController(IDockiUpDbContext db, ISecretsVaultService vault, INotificationSender sender) : ControllerBase
    {
        [HttpGet("Channels", Name = "ListNotificationChannels")]
        [ProducesResponseType(typeof(NotificationChannelDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<NotificationChannelDto[]>> ListNotificationChannels(CancellationToken cancellationToken)
        {
            var rows = await db.NotificationChannels.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
            return Ok(rows.Select(c => NotificationChannels.ToDto(c, vault)).ToArray());
        }

        [HttpPost("Channels", Name = "CreateNotificationChannel")]
        [ProducesResponseType(typeof(NotificationChannelDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<NotificationChannelDto>> CreateNotificationChannel([FromBody] SaveNotificationChannelRequest request, CancellationToken cancellationToken)
        {
            var channel = new NotificationChannel { Name = "", Type = request.Type };
            NotificationChannels.Apply(channel, request, vault);
            db.NotificationChannels.Add(channel);
            await db.SaveChangesAsync(cancellationToken);
            return Ok(NotificationChannels.ToDto(channel, vault));
        }

        [HttpPut("Channels/{id:guid}", Name = "UpdateNotificationChannel")]
        [ProducesResponseType(typeof(NotificationChannelDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<NotificationChannelDto>> UpdateNotificationChannel(Guid id, [FromBody] SaveNotificationChannelRequest request, CancellationToken cancellationToken)
        {
            var channel = await Find(id, cancellationToken);
            NotificationChannels.Apply(channel, request, vault);
            await db.SaveChangesAsync(cancellationToken);
            return Ok(NotificationChannels.ToDto(channel, vault));
        }

        [HttpDelete("Channels/{id:guid}", Name = "DeleteNotificationChannel")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteNotificationChannel(Guid id, CancellationToken cancellationToken)
        {
            db.NotificationChannels.Remove(await Find(id, cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        /// <summary>Sends a test message to the channel right away and reports what the service answered.</summary>
        [HttpPost("Channels/{id:guid}/Test", Name = "TestNotificationChannel")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> TestNotificationChannel(Guid id, CancellationToken cancellationToken)
        {
            var channel = await Find(id, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(NotificationDispatcher.SendTimeout);
            try
            {
                await sender.SendAsync(NotificationChannels.ToTarget(channel, vault),
                    new NotificationMessage(NotificationEvent.None, "DockiUp test notification",
                        $"If you can read this, the \"{channel.Name}\" channel works."), timeout.Token);
                return NoContent();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var reason = ex is OperationCanceledException ? "the service did not answer in time" : ex.Message;
                return BadRequest(new ProblemDetails { Title = "Test notification failed", Detail = $"Sending failed: {reason}", Status = 400 });
            }
        }

        private async Task<NotificationChannel> Find(Guid id, CancellationToken cancellationToken)
            => await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Notification channel not found.");
    }
}
