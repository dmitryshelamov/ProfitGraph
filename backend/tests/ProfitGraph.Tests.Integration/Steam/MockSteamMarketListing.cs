namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     One listing of the mock account: its asset, what the seller receives and whether it is confirmed.
/// </summary>
internal sealed class MockSteamMarketListing(string id, string assetId, long sellerPrice)
{
    public string Id { get; } = id;

    public string AssetId { get; } = assetId;

    public long SellerPrice { get; } = sellerPrice;

    public bool Confirmed { get; set; }
}