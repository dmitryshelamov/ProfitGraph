namespace ProfitGraph.Domain.Accounts;

public interface IAccountSnapshotRepository
{
    /// <summary>
    ///     Stores the snapshot unless its five-minute bucket already has one; returns whether it was stored.
    /// </summary>
    Task<bool> TryAdd(AccountSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>
    ///     The credential's snapshots captured in [<paramref name="from" />, <paramref name="to" />), oldest first.
    /// </summary>
    Task<IReadOnlyList<AccountSnapshot>> List(ExchangeCredentialId credentialId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     Keeps only the latest snapshot per credential and UTC hour among those captured before <paramref name="before" />;
    ///     returns how many it removed.
    /// </summary>
    Task<int> ThinToHourly(DateTimeOffset before, CancellationToken cancellationToken);
}