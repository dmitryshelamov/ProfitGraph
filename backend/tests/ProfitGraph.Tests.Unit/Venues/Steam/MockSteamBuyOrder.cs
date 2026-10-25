namespace ProfitGraph.Tests.Unit.Venues.Steam;

internal sealed class MockSteamBuyOrder(string marketHashName, int quantity, long priceTotal)
{
    public string MarketHashName { get; } = marketHashName;

    public int Quantity { get; } = quantity;

    public long PriceTotal { get; } = priceTotal;

    public int Remaining { get; set; } = quantity;

    public bool Active { get; set; } = true;
}