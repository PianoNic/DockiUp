using DockiUp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Domain.Models
{
    [Owned]
    public class UserSettings
    {
        public int Id { get; set; }
        public ColorScheme PreferredColorScheme { get; set; }
    }
}
