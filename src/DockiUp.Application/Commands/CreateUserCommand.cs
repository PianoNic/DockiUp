using DockiUp.Application.Dtos.User;
using DockiUp.Application.Mappers;
using DockiUp.Domain.Enums;
using DockiUp.Domain.Models;
using DockiUp.Infrastructure;
using MediatR;

namespace DockiUp.Application.Commands
{
    public class CreateUserCommand : IRequest<UserDto>
    {
        public CreateUserCommand(CreateUserDto userCreds)
        {
            UserCreds = userCreds;
        }

        public CreateUserDto UserCreds { get; set; }
    }

    public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
    {
        private readonly DockiUpDbContext _DbContext;

        public CreateUserCommandHandler(DockiUpDbContext dbContext)
        {
            _DbContext = dbContext;
        }

        public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
        {
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.UserCreds.Password);

            var newUser = new User
            {
                Username = request.UserCreds.Username,
                PasswordHash = hashedPassword,
                Email = request.UserCreds.Email,
                UserSettings = new UserSettings
                {
                    PreferredColorScheme = ColorScheme.System
                },
                UserRole = UserRole.User
            };

            var user = _DbContext.Users.Add(newUser);
            await _DbContext.SaveChangesAsync(cancellationToken);

            return user.Entity.ToDto();
        }
    }
}
