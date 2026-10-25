namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     An invented market history event in the shape Profit Fox Designer ADR 0087 describes: 1 listed, 2 cancelled, 3 sold,
///     4 bought. Money is in minor units of KZT.
/// </summary>
internal sealed record MockSteamHistoryEvent(
    int Type,
    string ListingId,
    long Seconds,
    string AssetId,
    string? PurchaseId = null,
    string? NewAssetId = null,
    long Paid = 0,
    long Fee = 0,
    long Received = 0,
    string Name = "Dreams & Nightmares Case");