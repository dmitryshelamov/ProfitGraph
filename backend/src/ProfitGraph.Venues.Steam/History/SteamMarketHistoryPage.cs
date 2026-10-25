namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     One page of market history, newest event first, with Steam's total count of events.
/// </summary>
internal sealed record SteamMarketHistoryPage(long Total, long Start, IReadOnlyList<SteamMarketReceipt> Receipts);