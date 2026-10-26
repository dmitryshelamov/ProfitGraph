using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Converters;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.RenameExchangeCredential;

internal sealed class RenameExchangeCredentialCommandHandler(IExchangeCredentialRepository credentials)
    : IRequestHandler<RenameExchangeCredentialCommand, ExchangeCredentialInternal>
{
    public async Task<ExchangeCredentialInternal> Handle(RenameExchangeCredentialCommand command, CancellationToken cancellationToken)
    {
        ExchangeCredential credential = await credentials.GetOwned(command.Id, command.OwnerId, cancellationToken);

        if (credential.Rename(new ExchangeCredentialLabel(command.Label)))
        {
            await credentials.Update(credential, cancellationToken);
        }

        return credential.ToInternal();
    }
}