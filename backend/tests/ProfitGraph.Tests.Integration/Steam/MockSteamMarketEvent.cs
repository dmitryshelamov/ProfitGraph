namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     One market history event of the mock account (Profit Fox Designer ADR 0087): 1 listed, 2 cancelled, 3 sold, 4 bought.
/// </summary>
internal sealed record MockSteamMarketEvent(
    int Type,
    string ListingId,
    long Seconds,
    string AssetId,
    string? NewAssetId,
    string? PurchaseId,
    long Paid,
    long Fee,
    long Received);