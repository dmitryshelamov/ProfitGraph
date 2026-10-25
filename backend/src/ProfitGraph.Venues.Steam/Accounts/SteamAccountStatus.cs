using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     A Steam account's Guard status and when its eligibility is checked next (ADR 0022).
/// </summary>
internal sealed class SteamAccountStatus(IServiceScopeFactory scopes) : IVenueAccountStatus
{
    public VenueId Venue => SteamVenue.VenueId;

    public async Task<VenueAccountStatusInternal?> Read(Guid credentialId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        SteamAccount? account = await scope.ServiceProvider.GetRequiredService<ISteamAccountStore>().Get(credentialId, cancellationToken);

        return account is null ? null : new VenueAccountStatusInternal(account.GuardStatus.ToString(), account.NextCheckAt);
    }
}