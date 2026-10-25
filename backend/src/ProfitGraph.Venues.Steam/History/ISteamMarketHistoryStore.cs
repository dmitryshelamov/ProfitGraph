namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     The account's market history as read so far (Profit Fox Designer ADR 0087): receipts by position, written once.
/// </summary>
internal interface ISteamMarketHistoryStore
{
    /// <summary>
    ///     The position after the newest stored receipt, or null before the account's history was first read.
    /// </summary>
    Task<long?> Next(Guid credentialId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SteamStoredReceipt>> From(Guid credentialId, long position, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores new receipts when the stored history still ends at <paramref name="expectedNext" />; false when another
    ///     reader stored first.
    /// </summary>
    Task<bool> Append(Guid credentialId, long? expectedNext, IReadOnlyList<SteamStoredReceipt> receipts, CancellationToken cancellationToken);
}