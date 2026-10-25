namespace ProfitGraph.Venues.Steam.Inventory;

/// <summary>
///     One item in an account's inventory: where it lies (context and asset ID, kept as Steam's strings), which market item
///     it is, whether Steam lets it be listed, and when it unlocks. <see cref="UnlockAt" /> is null when the unlock time is
///     not known; such an item is treated as locked.
/// </summary>
internal sealed record SteamInventoryAsset(int AppId, string ContextId, string AssetId, string MarketHashName, int Amount, bool Marketable, DateTimeOffset? UnlockAt)
{
    public bool SellableAt(DateTimeOffset now)
    {
        return Marketable && UnlockAt is { } unlock && unlock <= now;
    }
}