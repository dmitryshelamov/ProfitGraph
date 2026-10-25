namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     A market history event as Profit Fox Designer ADR 0087 normalized it: event types 1 to 4.
/// </summary>
internal enum SteamMarketReceiptKind
{
    Listed,
    Cancelled,
    Sold,
    Bought
}