namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     The write-once attribution of an account's market history receipts to bot orders (Profit Fox Designer ADR 0087).
/// </summary>
internal interface ISteamMarketAttributions
{
    Task<IReadOnlyList<SteamAttribution>> Of(Guid credentialId, CancellationToken cancellationToken);

    /// <summary>
    ///     Records the attributions, all or nothing; false when a receipt is attributed already.
    /// </summary>
    Task<bool> Attribute(Guid credentialId, IReadOnlyList<SteamAttribution> attributions, DateTimeOffset at, CancellationToken cancellationToken);
}