using ProfitGraph.Venues.Steam.Accounts;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

internal sealed class FakeSteamAccountStore : ISteamAccountStore
{
    public SteamGuardStatus Status { get; set; } = SteamGuardStatus.Ready;

    public Task<SteamAccount?> Get(Guid credentialId, CancellationToken cancellationToken)
    {
        return Task.FromResult<SteamAccount?>(
            new SteamAccount(credentialId, "fingerprint", Status, NextCheckAt: null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
    }
}