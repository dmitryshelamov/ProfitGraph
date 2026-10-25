using ProfitGraph.Venues.Steam.Accounts;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     One Steam account's state in memory, with the store's compare-and-set on its status.
/// </summary>
internal sealed class FakeSteamAccountStore : ISteamAccountStore
{
    public SteamGuardStatus Status { get; set; } = SteamGuardStatus.Ready;

    public DateTimeOffset? NextCheckAt { get; set; }

    public Task<SteamAccount?> Get(Guid credentialId, CancellationToken cancellationToken)
    {
        return Task.FromResult<SteamAccount?>(Account(credentialId));
    }

    public Task<IReadOnlyList<SteamAccount>> Due(DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SteamAccount>>(Status == SteamGuardStatus.Checking && NextCheckAt <= now ? [Account(SteamSessionHarness.Credential)] : []);
    }

    public Task<bool> Record(
        Guid credentialId,
        SteamGuardStatus expected,
        SteamGuardStatus status,
        DateTimeOffset? nextCheckAt,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (Status != expected)
        {
            return Task.FromResult(false);
        }

        Status = status;
        NextCheckAt = nextCheckAt;

        return Task.FromResult(true);
    }

    private SteamAccount Account(Guid credentialId)
    {
        return new SteamAccount(credentialId, "fingerprint", Status, NextCheckAt, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }
}