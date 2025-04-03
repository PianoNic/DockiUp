using DockiUp.Application.Dtos;
using DockiUp.Domain.Models;

namespace DockiUp.Application.Mappers
{
    /// <summary>
    /// Provides extension methods for mapping between Container and ContainerDto.
    /// </summary>
    public static class ContainerMapper
    {
        /// <summary>
        /// Converts a Container entity to a ContainerDto.
        /// </summary>
        public static ContainerDto? ToDto(this Container? container)
        {
            if (container == null)
                return null;

            return new ContainerDto
            {
                DbContainerId = container.Id,
                ContainerId = container.ContainerId,
                Name = container.Name,
                Description = container.Description,
                GitUrl = container.GitUrl,
                Path = container.Path,
                LastUpdated = container.LastUpdated,
                LastGitPush = container.LastGitPush,
                Status = container.Status,
                UpdateMethod = container.UpdateMethod,
                WebhookSecret = container.WebhookSecret,
                CheckIntervals = container.CheckIntervals
            };
        }

        /// <summary>
        /// Converts a ContainerDto to a Container entity.
        /// </summary>
        public static Container? ToEntity(this ContainerDto? dto)
        {
            if (dto == null)
                return null;

            return new Container
            {
                Id = dto.DbContainerId,
                ContainerId = string.IsNullOrEmpty(dto.ContainerId) ? null : dto.ContainerId,
                Name = dto.Name,
                Description = dto.Description,
                GitUrl = dto.GitUrl,
                Path = dto.Path,
                LastUpdated = dto.LastUpdated,
                LastGitPush = dto.LastGitPush,
                Status = dto.Status,
                UpdateMethod = dto.UpdateMethod,
                WebhookSecret = dto.WebhookSecret,
                CheckIntervals = dto.CheckIntervals
            };
        }
    }
}
