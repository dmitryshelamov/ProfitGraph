namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     An item a bot owns on its account: bought by the bot, still in the inventory, sellable from <see cref="UnlockAt" />.
/// </summary>
internal sealed record SteamOwnedAsset(int AppId, string ContextId, string AssetId, DateTimeOffset UnlockAt);