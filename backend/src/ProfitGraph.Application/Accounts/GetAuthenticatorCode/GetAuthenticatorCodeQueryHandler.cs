using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Accounts.GetAuthenticatorCode;

/// <summary>
///     The current authenticator code of the owner's own credential; a venue without authenticators has none.
/// </summary>
internal sealed class GetAuthenticatorCodeQueryHandler(IExchangeCredentialRepository credentials, VenuePorts<IVenueAuthenticatorCodes> codes)
    : IRequestHandler<GetAuthenticatorCodeQuery, AuthenticatorCodeInternal>
{
    public async Task<AuthenticatorCodeInternal> Handle(GetAuthenticatorCodeQuery query, CancellationToken cancellationToken)
    {
        ExchangeCredential credential = await credentials.GetOwned(query.CredentialId, query.OwnerId, cancellationToken);

        return codes.Find(credential.Venue) is { } venue
            ? await venue.Current(query.OwnerId, query.CredentialId, cancellationToken)
            : throw new ResourceNotFoundException("The credential has no authenticator.", ExchangeCredentialErrorCodes.AuthenticatorUnavailable);
    }
}