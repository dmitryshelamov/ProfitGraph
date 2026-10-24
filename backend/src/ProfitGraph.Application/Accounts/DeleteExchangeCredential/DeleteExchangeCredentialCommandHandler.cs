using MediatR;

using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.DeleteExchangeCredential;

internal sealed class DeleteExchangeCredentialCommandHandler(IExchangeCredentialRepository credentials, ISecretProtector protector)
    : IRequestHandler<DeleteExchangeCredentialCommand>
{
    public async Task Handle(DeleteExchangeCredentialCommand command, CancellationToken cancellationToken)
    {
        protector.EnsureAvailable();

        if (command.Id == Guid.Empty || !await credentials.Delete(new ExchangeCredentialId(command.Id), new UserId(command.OwnerId), cancellationToken))
        {
            throw OwnedExchangeCredentials.NotFound();
        }
    }
}