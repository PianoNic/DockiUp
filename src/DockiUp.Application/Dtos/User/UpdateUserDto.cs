namespace DockiUp.Application.Dtos.User
{
    public class UpdateUserDto
    {
        public required int UserId { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public byte[]? ProfilePicture { get; set; }
        public UserSettingsDto? UserSettings { get; set; }
    }
}
