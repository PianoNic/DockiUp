using DockiUp.Application.Dtos.User;
using DockiUp.Application.Mappers;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Queries
{
    public class GetUserQuery : IRequest<UserDto>
    {
        public GetUserQuery(int userId)
        {
            UserId = userId;
        }

        public int UserId { get; set; }
    }

    public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
    {
        private readonly DockiUpDbContext _DbContext;

        public GetUserQueryHandler(DockiUpDbContext dbContext)
        {
            _DbContext = dbContext;
        }

        public async Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken)
        {
            var user = await _DbContext.Users.Include(u => u.UserSettings).SingleAsync(u => u.Id == request.UserId);
            return user.ToDto();
        }
    }
}
