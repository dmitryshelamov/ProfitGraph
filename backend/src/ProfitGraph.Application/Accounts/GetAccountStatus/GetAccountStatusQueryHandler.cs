using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts.Credentials;

namespace ProfitGraph.Application.Accounts.GetAccountStatus;

/// <summary>
///     The venue's own status of the owner's credential; a venue without one answers <c>account_status_unavailable</c>.
/// </summary>
internal sealed class GetAccountStatusQueryHandler(IExchangeCredentialRepository credentials, VenuePorts<IVenueAccountStatus> statuses)
    : IRequestHandler<GetAccountStatusQuery, VenueAccountStatusInternal>
{
    public async Task<VenueAccountStatusInternal> Handle(GetAccountStatusQuery query, CancellationToken cancellationToken)
    {
        ExchangeCredential credential = await credentials.GetOwned(query.CredentialId, query.OwnerId, cancellationToken);

        return statuses.Find(credential.Venue) is { } venue && await venue.Read(credential.Id.Value, cancellationToken) is { } status
            ? status
            : throw new ResourceNotFoundException("The venue keeps no status of this account.", ExchangeCredentialErrorCodes.AccountStatusUnavailable);
    }
}