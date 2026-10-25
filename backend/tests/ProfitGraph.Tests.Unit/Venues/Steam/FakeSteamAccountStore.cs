using ProfitGraph.Venues.Steam.Accounts;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Steam accounts in memory: the one a test sets up, with the store's compare-and-set on its status, and those added.
/// </summary>
internal sealed class FakeSteamAccountStore : ISteamAccountStore
{
    public SteamGuardStatus Status { get; set; } = SteamGuardStatus.Ready;

    public DateTimeOffset? NextCheckAt { get; set; }

    public List<SteamAccount> Added { get; } = new List<SteamAccount>();

    public Task<SteamAccount?> Get(Guid credentialId, CancellationToken cancellationToken)
    {
        return Task.FromResult<SteamAccount?>(Account(credentialId));
    }

    public Task<IReadOnlyList<SteamAccount>> Due(DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SteamAccount>>(
            Status is SteamGuardStatus.Checking or SteamGuardStatus.CoolingDown && NextCheckAt <= now ? [Account(SteamSessionHarness.Credential)] : []);
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

    public Task<bool> AnyFingerprint(IReadOnlyList<string> fingerprints, CancellationToken cancellationToken)
    {
        return Task.FromResult(Added.Any(account => fingerprints.Contains(account.AccountFingerprint)));
    }

    public Task<bool> Add(SteamAccount account, CancellationToken cancellationToken)
    {
        if (Added.Any(other => other.AccountFingerprint == account.AccountFingerprint))
        {
            return Task.FromResult(false);
        }

        Added.Add(account);

        return Task.FromResult(true);
    }

    private SteamAccount Account(Guid credentialId)
    {
        return new SteamAccount(credentialId, "fingerprint", Status, NextCheckAt, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }
}