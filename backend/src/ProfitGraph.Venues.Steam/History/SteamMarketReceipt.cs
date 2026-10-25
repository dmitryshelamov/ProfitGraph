namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     One market history event of an account, every reference resolved inside its page (Profit Fox Designer ADR 0087). The
///     ID (<c>type:listing:purchase:seconds:fraction</c>) is stable across pages and restarts. The asset is the listed item,
///     for a cancel the returned one, for a buy the bought item under its new owner's ID. Money is in whole minor units of
///     the wallet currency: what the buyer paid and what the seller received.
/// </summary>
internal sealed record SteamMarketReceipt(
    string Id,
    SteamMarketReceiptKind Kind,
    string ListingId,
    int AppId,
    string MarketHashName,
    string ContextId,
    string AssetId,
    int Quantity,
    long BuyerPaysMinorUnits,
    long SellerReceivesMinorUnits,
    DateTimeOffset At);