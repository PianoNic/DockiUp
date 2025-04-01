using DockiUp.Application.Dtos;
using DockiUp.Domain.Enums;
using DockiUp.Domain.Models;
using MediatR;

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
        public async Task<ContainerDto[]> Handle(GetContainersQuery request, CancellationToken cancellationToken)
        {
            return await Task.FromResult(new ContainerDto[]
            {
                new ContainerDto
                {
                    DbContainerId = 1,
                    ContainerId = "abc123",
                    Name = "Web Application",
                    Description = "Main web application container",
                    GitUrl = "https://github.com/username/webapp",
                    Path = "/containers/webapp",
                    LastUpdated = DateTime.Now.AddDays(-1),
                    LastGitPush = DateTime.Now.AddDays(-2),
                    WebhookSecret = new WebhookSecret
                    {
                        Id = 1,
                        Identifier = "webhook-1",
                        Secret = "s3cr3t-k3y-1"
                    },
                    Status = StatusType.Running,
                    UpdateMethod = UpdateMethod.UpdateManually
                },
                new ContainerDto
                {
                    DbContainerId = 2,
                    ContainerId = "def456",
                    Name = "API Service",
                    Description = "RESTful API service container",
                    GitUrl = "https://github.com/username/api-service",
                    Path = "/containers/api",
                    LastUpdated = DateTime.Now.AddHours(-12),
                    LastGitPush = DateTime.Now.AddDays(-1),
                    CheckIntervals = 30,
                    Status = StatusType.Updating,
                    UpdateMethod = UpdateMethod.CheckPeriodically
                },
                new ContainerDto
                {
                    DbContainerId = 3,
                    ContainerId = "ghi789",
                    Name = "Database",
                    Description = "PostgreSQL database container",
                    GitUrl = "https://github.com/username/db-config",
                    Path = "/containers/database",
                    LastUpdated = DateTime.Now.AddDays(-3),
                    LastGitPush = DateTime.Now.AddDays(-3),
                    WebhookSecret = new WebhookSecret
                    {
                        Id = 2,
                        Identifier = "webhook-2",
                        Secret = "s3cr3t-k3y-2"
                    },
                    CheckIntervals = 120,
                    Status = StatusType.Updating,
                    UpdateMethod = UpdateMethod.UseWebhook
                }
            });
        }
    }
}
