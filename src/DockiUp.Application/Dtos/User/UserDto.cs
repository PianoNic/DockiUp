using DockiUp.Domain.Enums;

namespace DockiUp.Application.Dtos.User
{
    public class UserDto
    {
        public int UserId { get; set; }
        public required string Username { get; set; }
        public required string Email { get; set; }
        public byte[]? ProfilePicture { get; set; }
        public required UserSettingsDto UserSettings { get; set; }
        public required UserRole UserRole { get; set; }
    }
}
