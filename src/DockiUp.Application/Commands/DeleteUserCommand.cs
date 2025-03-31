using DockiUp.Infrastructure;
using MediatR;

namespace DockiUp.Application.Commands
{
    public class DeleteUserCommand : IRequest
    {
        public DeleteUserCommand(int userId)
        {
            UserId = userId;
        }

        public int UserId { get; set; }
    }

    public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
    {
        private readonly DockiUpDbContext _DbContext;

        public DeleteUserCommandHandler(DockiUpDbContext dbContext)
        {
            _DbContext = dbContext;
        }

        public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
        {
            var user = _DbContext.Users.Single(u => u.Id == request.UserId);
            _DbContext.Users.Remove(user);
            await _DbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
