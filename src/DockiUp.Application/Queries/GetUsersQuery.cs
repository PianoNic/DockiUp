using DockiUp.Application.Dtos.User;
using DockiUp.Application.Mappers;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Queries
{
    public class GetUsersQuery : IRequest<UserDto[]>
    {
        public GetUsersQuery()
        {
        }
    }

    public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, UserDto[]>
    {
        private readonly DockiUpDbContext _DbContext;

        public GetUsersQueryHandler(DockiUpDbContext dbContext)
        {
            _DbContext = dbContext;
        }

        public async Task<UserDto[]> Handle(GetUsersQuery request, CancellationToken cancellationToken)
        {
            var users = await _DbContext.Users.AsNoTracking().Include(u => u.UserSettings).ToArrayAsync(cancellationToken);
            return users.Select(u => u.ToDto()).ToArray();
        }
    }
}
