using MediatR;

namespace DockiUp.Application.Commands
{
    public class CreateWebhookCommand : IRequest<string>
    {
    }

    public class CreateWebhookCommandHandler : IRequestHandler<CreateWebhookCommand, string>
    {
        public async Task<string> Handle(CreateWebhookCommand request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
