using DockiUp.Domain.Enums;

namespace DockiUp.Domain.Models
{
    public class User
    {
        public int Id { get; set; }
        public required string Username { get; set; }
        public required string PasswordHash { get; set; }
        public required string Email { get; set; }
        public UserRole UserRole { get; set; }
        public byte[]? ProfilePicture { get; set; }

        public UserSettings UserSettings { get; set; }
    }
}
