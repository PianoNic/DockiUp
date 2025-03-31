using DockiUp.Domain.Enums;

namespace DockiUp.Application.Dtos.User
{
    public class UserSettingsDto
    {
        public int UserSettingsId { get; set; }
        public required ColorScheme PreferredColorScheme { get; set; }
    }
}
