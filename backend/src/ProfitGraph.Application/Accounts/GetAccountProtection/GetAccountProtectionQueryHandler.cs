using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.GetAccountProtection;

/// <summary>
///     A venue without account protection reports a clear account.
/// </summary>
internal sealed class GetAccountProtectionQueryHandler(IExchangeCredentialRepository credentials, VenuePorts<IVenueAccountProtection> protections)
    : IRequestHandler<GetAccountProtectionQuery, VenueAccountProtectionInternal>
{
    public async Task<VenueAccountProtectionInternal> Handle(GetAccountProtectionQuery query, CancellationToken cancellationToken)
    {
        ExchangeCredential credential = await credentials.GetOwned(query.CredentialId, query.OwnerId, cancellationToken);

        return protections.Find(credential.Venue) is { } protection
            ? await protection.Read(credential.Id.Value, cancellationToken)
            : VenueAccountProtectionInternal.Clear;
    }
}