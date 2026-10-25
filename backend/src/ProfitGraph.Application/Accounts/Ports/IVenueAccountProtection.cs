using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Accounts.Ports;

/// <summary>
///     A venue's protection of one account (ADR 0022, C7). When it opens, the module stops every bot of the credential with
///     <c>VenueAccountProtection</c>; the owner reads why and resumes it here, which re-arms those bots.
/// </summary>
public interface IVenueAccountProtection : IVenuePort
{
    Task<VenueAccountProtectionInternal> Read(Guid credentialId, CancellationToken cancellationToken);

    Task Resume(Guid credentialId, CancellationToken cancellationToken);
}