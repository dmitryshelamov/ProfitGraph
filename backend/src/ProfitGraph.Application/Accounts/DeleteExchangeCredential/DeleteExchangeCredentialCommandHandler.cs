using MediatR;

using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Accounts.DeleteExchangeCredential;

internal sealed class DeleteExchangeCredentialCommandHandler(IExchangeCredentialRepository credentials, IBotRepository bots, ISecretProtector protector)
    : IRequestHandler<DeleteExchangeCredentialCommand>
{
    public async Task Handle(DeleteExchangeCredentialCommand command, CancellationToken cancellationToken)
    {
        protector.EnsureAvailable();

        if (command.Id != Guid.Empty && await bots.AnyUsesCredential(new ExchangeCredentialId(command.Id), cancellationToken))
        {
            ExchangeCredential? used = await credentials.Get(new ExchangeCredentialId(command.Id), cancellationToken);

            if (used is not null && used.IsOwnedBy(new UserId(command.OwnerId)))
            {
                throw new StateConflictException("A bot trades through this credential; delete the bot first.", BotErrorCodes.CredentialInUse);
            }
        }

        if (command.Id == Guid.Empty || !await credentials.Delete(new ExchangeCredentialId(command.Id), new UserId(command.OwnerId), cancellationToken))
        {
            throw OwnedExchangeCredentials.NotFound();
        }
    }
}