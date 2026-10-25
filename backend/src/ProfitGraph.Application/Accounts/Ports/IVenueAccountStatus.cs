using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Accounts.Ports;

/// <summary>
///     Where a venue account stands on the venue's own terms, such as an authenticator that is still being checked (ADR
///     0022): a status code the client shows, and when the venue looks again. A venue without this port has none.
/// </summary>
public interface IVenueAccountStatus : IVenuePort
{
    Task<VenueAccountStatusInternal?> Read(Guid credentialId, CancellationToken cancellationToken);
}