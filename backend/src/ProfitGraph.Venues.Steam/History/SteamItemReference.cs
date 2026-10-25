namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     An item a history event names, resolved to its description: the app, its hash name, where it was and how many.
/// </summary>
internal readonly record struct SteamItemReference(int AppId, string MarketHashName, string ContextId, string AssetId, int Quantity)
{
    public SteamMarketReceipt Receipt(
        string id,
        SteamMarketReceiptKind kind,
        string listingId,
        string contextId,
        string assetId,
        long buyer,
        long seller,
        DateTimeOffset at)
    {
        return new SteamMarketReceipt(id, kind, listingId, AppId, MarketHashName, contextId, assetId, Quantity, buyer, seller, at);
    }
}