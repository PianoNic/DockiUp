using DockiUp.Application.Dtos;
using DockiUp.Domain.Models;

namespace DockiUp.Application.Mappers
{
    /// <summary>
    /// Contains extension methods for mapping between WebhookSecret DTO and Entity
    /// </summary>
    public static class WebhookSecretMapper
    {
        /// <summary>
        /// Maps a WebhookSecret entity to a WebhookSecret DTO
        /// </summary>
        /// <param name="entity">The entity to map from</param>
        /// <returns>A WebhookSecret DTO</returns>
        public static WebhookSecretDto? ToDto(this WebhookSecret? entity)
        {
            if (entity == null)
                return null;

            return new WebhookSecretDto
            {
                WebhookSecretDtoId = entity.Id,
                Identifier = entity.Identifier,
                Secret = entity.Secret
            };
        }

        /// <summary>
        /// Maps a WebhookSecret DTO to a WebhookSecret entity
        /// </summary>
        /// <param name="dto">The DTO to map from</param>
        /// <returns>A WebhookSecret entity</returns>
        public static WebhookSecret? ToEntity(this WebhookSecretDto? dto)
        {
            if (dto == null)
                return null;

            return new WebhookSecret
            {
                Id = dto.WebhookSecretDtoId,
                Identifier = dto.Identifier,
                Secret = dto.Secret
            };
        }
    }
}