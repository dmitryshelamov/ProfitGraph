namespace ProfitGraph.Infrastructure.Scanner;

/// <summary>
///     Section <c>Scanner</c>: how the Worker's <c>scanner</c> role keeps its streams, backfills and snapshot (ADR 0059).
/// </summary>
internal sealed class ScannerOptions
{
    internal const string SectionName = "Scanner";

    /// <summary>
    ///     How long after a minute closes the snapshot is computed, so the minute's closing candles arrive first.
    /// </summary>
    public TimeSpan SnapshotDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How often the venue's universe is read again; streams restart only when it changed.
    /// </summary>
    public TimeSpan UniverseRefreshInterval { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     A stream silent this long counts as lost and connects again.
    /// </summary>
    public TimeSpan StreamStallTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     How long the history waits for the stream's first message, so the subscription comes before the history.
    /// </summary>
    public TimeSpan SubscribeWait { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How often a connected stream's pairs that wait for history or a backfill are tried again.
    /// </summary>
    public TimeSpan RepairInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The pause after a failed venue request.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How often another Worker tries to take the scanner over, and how often the holder checks its lock connection.
    /// </summary>
    public TimeSpan LockCheckInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     A snapshot older than this makes readiness Degraded.
    /// </summary>
    public TimeSpan SnapshotMaxAge { get; init; } = TimeSpan.FromMinutes(5);

    public bool IsValid()
    {
        return SnapshotDelay >= TimeSpan.Zero && SnapshotDelay < TimeSpan.FromSeconds(50) && UniverseRefreshInterval >= TimeSpan.FromMinutes(1) &&
            ReconnectDelay > TimeSpan.Zero && MaxReconnectDelay >= ReconnectDelay && StreamStallTimeout >= TimeSpan.FromSeconds(10) &&
            SubscribeWait >= TimeSpan.Zero && RepairInterval > TimeSpan.Zero && RetryDelay > TimeSpan.Zero && LockCheckInterval > TimeSpan.Zero &&
            SnapshotMaxAge > TimeSpan.FromMinutes(1);
    }
}