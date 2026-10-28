using MediatR;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts.Credentials;

namespace ProfitGraph.Application.Accounts.ResumeAccountProtection;

internal sealed class ResumeAccountProtectionCommandHandler(IExchangeCredentialRepository credentials, VenuePorts<IVenueAccountProtection> protections)
    : IRequestHandler<ResumeAccountProtectionCommand>
{
    public async Task Handle(ResumeAccountProtectionCommand command, CancellationToken cancellationToken)
    {
        ExchangeCredential credential = await credentials.GetOwned(command.CredentialId, command.OwnerId, cancellationToken);

        if (protections.Find(credential.Venue) is { } protection)
        {
            await protection.Resume(credential.Id.Value, cancellationToken);
        }
    }
}