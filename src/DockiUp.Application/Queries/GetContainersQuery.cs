using DockiUp.Application.Dtos;
using DockiUp.Application.Mappers;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Queries
{
    public class GetContainersQuery : IRequest<ContainerDto[]>
    {
        public GetContainersQuery()
        {
        }
    }

    public class GetContainersQueryHandler : IRequestHandler<GetContainersQuery, ContainerDto[]>
    {
        public GetContainersQueryHandler(DockiUpDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        private DockiUpDbContext _dbContext { get; set; }

        public async Task<ContainerDto[]> Handle(GetContainersQuery request, CancellationToken cancellationToken)
        {
            var containers = await _dbContext.Containers.ToArrayAsync(cancellationToken);
            return containers.Select(a => a.ToDto()).ToArray();
        }
    }
}
