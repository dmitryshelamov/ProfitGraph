namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     One of the account's buy orders as the market page lists it: Steam's ID and the item.
/// </summary>
internal sealed record SteamAccountBuyOrder(string Id, int AppId, string MarketHashName);