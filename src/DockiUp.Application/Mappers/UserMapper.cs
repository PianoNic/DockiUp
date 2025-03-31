using DockiUp.Application.Dtos.User;
using DockiUp.Domain.Models;

namespace DockiUp.Application.Mappers
{
    /// <summary>
    /// Contains extension methods for mapping between User domain models and DTOs
    /// </summary>
    public static class UserMapper
    {
        /// <summary>
        /// Converts a User entity to a UserDto
        /// </summary>
        /// <param name="user">The User entity to convert</param>
        /// <returns>A UserDto representation of the User, or null if the input is null</returns>
        public static UserDto? ToDto(this User? user)
        {
            if (user == null)
                return null;

            return new UserDto
            {
                UserId = user.Id,
                Username = user.Username,
                Email = user.Email,
                ProfilePicture = user.ProfilePicture,
                UserSettings = user.UserSettings.ToDto(),
                UserRole = user.UserRole
            };
        }

        /// <summary>
        /// Converts a UserDto to a User entity
        /// </summary>
        /// <param name="userDto">The UserDto to convert</param>
        /// <returns>A User entity representation of the DTO, or null if the input is null</returns>
        public static User? ToEntity(this UserDto? userDto)
        {
            if (userDto == null)
                return null;

            return new User
            {
                Id = userDto.UserId,
                Username = userDto.Username,
                Email = userDto.Email,
                ProfilePicture = userDto.ProfilePicture,
                PasswordHash = string.Empty,
                UserSettings = userDto.UserSettings.ToEntity(),
                UserRole = userDto.UserRole
            };
        }

        /// <summary>
        /// Converts a UserSettings entity to a UserSettingsDto
        /// </summary>
        /// <param name="userSettings">The UserSettings entity to convert</param>
        /// <returns>A UserSettingsDto representation of the UserSettings, or null if the input is null</returns>
        public static UserSettingsDto? ToDto(this UserSettings? userSettings)
        {
            if (userSettings == null)
                return null;

            return new UserSettingsDto
            {
                UserSettingsId = userSettings.Id,
                PreferredColorScheme = userSettings.PreferredColorScheme
            };
        }

        /// <summary>
        /// Converts a UserSettingsDto to a UserSettings entity
        /// </summary>
        /// <param name="userSettingsDto">The UserSettingsDto to convert</param>
        /// <returns>A UserSettings entity representation of the DTO, or null if the input is null</returns>
        public static UserSettings? ToEntity(this UserSettingsDto? userSettingsDto)
        {
            if (userSettingsDto == null)
                return null;

            return new UserSettings
            {
                Id = userSettingsDto.UserSettingsId,
                PreferredColorScheme = userSettingsDto.PreferredColorScheme
            };
        }
    }
}
