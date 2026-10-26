using ProfitGraph.Application.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Application.Bots;

public sealed class SellPriceFloorTests
{
    private const decimal Close = 600m;

    [Test]
    public void ALimitSellBelowHalfTheCloseIsBelowTheFloor()
    {
        SellPriceFloor.IsBelow(Sell(OrderType.Limit, limitPrice: 299m), Close).ShouldBeTrue();
        SellPriceFloor.IsBelow(Sell(OrderType.Limit, limitPrice: 300m), Close).ShouldBeFalse();
        SellPriceFloor.IsBelow(Sell(OrderType.Limit, limitPrice: 692m), Close).ShouldBeFalse();
    }

    [Test]
    public void AMarketSellAndABuyAreNotFloored()
    {
        SellPriceFloor.IsBelow(Sell(OrderType.Market, limitPrice: null), Close).ShouldBeFalse();
        SellPriceFloor.IsBelow(Sell(OrderType.Limit, limitPrice: 1m) with { Side = OrderSide.Buy }, Close).ShouldBeFalse();
    }

    private static OrderSnapshot Sell(OrderType type, decimal? limitPrice)
    {
        return new OrderSnapshot(
            Id: 1,
            "exit",
            OrderSide.Sell,
            type,
            Quantity: 1m,
            limitPrice,
            QuoteBudget: null,
            Reservation: 1m,
            "exit",
            EligibleFromCandleIndex: 1,
            OrderStatus.PendingSubmit);
    }
}