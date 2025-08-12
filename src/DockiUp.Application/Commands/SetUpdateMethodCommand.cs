using DockiUp.Domain.Enums;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.Application.Commands
{
    public class SetUpdateMethodCommand : IRequest
    {
        public SetUpdateMethodCommand(UpdateMethod updateMethod, int containerId)
        {
            UpdateMethod = updateMethod;
            ContainerId = containerId;
        }

        public UpdateMethod UpdateMethod { get; set; }
        public int ContainerId { get; set; }
    }

    public class SetUpdateMethodCommandHandler : IRequestHandler<SetUpdateMethodCommand>
    {
        public SetUpdateMethodCommandHandler(DockiUpDbContext dbContext)
        {
            _DbContext = dbContext;
        }

        public DockiUpDbContext _DbContext { get; set; }

        public async Task Handle(SetUpdateMethodCommand request, CancellationToken cancellationToken)
        {
            var container = await _DbContext.Containers.AsTracking().SingleOrDefaultAsync(c => c.Id == request.ContainerId, cancellationToken) ?? throw new ArgumentException("Container does not exist");
            container.UpdateMethod = request.UpdateMethod;
            await _DbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
