namespace ProfitGraph.Venues.Steam.Inventory;

/// <summary>
///     One page of a context; <see cref="NextAssetId" /> continues it when Steam says more items follow.
/// </summary>
internal sealed record SteamInventoryPage(IReadOnlyList<SteamInventoryAsset> Assets, string? NextAssetId);