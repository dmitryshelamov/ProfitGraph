namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     One buy order of the mock account: price per item in minor units and items still wanted.
/// </summary>
internal sealed class MockSteamMarketBuyOrder(long price, int quantity)
{
    public long Price { get; } = price;

    public int Quantity { get; } = quantity;

    public int Remaining { get; set; } = quantity;

    public bool Active { get; set; } = true;
}