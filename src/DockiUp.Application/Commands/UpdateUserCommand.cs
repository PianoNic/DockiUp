using DockiUp.Application.Dtos.User;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Commands
{
    public class UpdateUserCommand : IRequest
    {
        public UpdateUserCommand(UpdateUserDto updateUserDto)
        {
            UpdateUserDto = updateUserDto;
        }

        public UpdateUserDto UpdateUserDto { get; set; }
    }

    public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand>
    {
        private readonly DockiUpDbContext _DbContext;

        public UpdateUserCommandHandler(DockiUpDbContext dbContext)
        {
            _DbContext = dbContext;
        }

        public async Task Handle(UpdateUserCommand request, CancellationToken cancellationToken)
        {
            var user = await _DbContext.Users
                .Include(u => u.UserSettings)
                .AsTracking()
                .SingleAsync(u => u.Id == request.UpdateUserDto.UserId, cancellationToken);

            if (request.UpdateUserDto.Username != null)
                user.Username = request.UpdateUserDto.Username;

            if (request.UpdateUserDto.Email != null)
                user.Email = request.UpdateUserDto.Email;

            if (request.UpdateUserDto.ProfilePicture != null)
                user.ProfilePicture = request.UpdateUserDto.ProfilePicture;

            if (request.UpdateUserDto.UserSettings != null)
                user.UserSettings.PreferredColorScheme = request.UpdateUserDto.UserSettings.PreferredColorScheme;

            await _DbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
