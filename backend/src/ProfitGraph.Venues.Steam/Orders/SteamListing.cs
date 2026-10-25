namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     One of the account's listings on <c>mylistings</c>: active, or still awaiting its mobile confirmation.
/// </summary>
internal sealed record SteamListing(string Id, int AppId, string ContextId, string AssetId, bool AwaitingConfirmation);