namespace ProfitGraph.Tests.Unit.Venues.Steam;

internal sealed class MockSteamListing(string id, int appId, string contextId, string assetId, long sellerPrice)
{
    public string Id { get; } = id;

    public int AppId { get; } = appId;

    public string ContextId { get; } = contextId;

    public string AssetId { get; } = assetId;

    public long SellerPrice { get; } = sellerPrice;

    public bool Confirmed { get; set; }
}