using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Accounts.Ports;

/// <summary>
///     A venue whose credentials hold an authenticator shows its current code on the owner's request (ADR 0022): made on
///     demand from the stored secret, never stored, logged or sent anywhere else. A venue without this port has none.
/// </summary>
public interface IVenueAuthenticatorCodes : IVenuePort
{
    Task<AuthenticatorCodeInternal> Current(Guid ownerId, Guid credentialId, CancellationToken cancellationToken);
}