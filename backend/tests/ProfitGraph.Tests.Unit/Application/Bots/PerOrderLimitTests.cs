using ProfitGraph.Application.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Application.Bots;

public sealed class PerOrderLimitTests
{
    private const decimal Limit = 600m;

    [Test]
    public void ALimitBuyAboveTheLimitExceedsIt()
    {
        PerOrderLimit.Exceeds(Order(OrderSide.Buy, OrderType.Limit, quantity: 2m, limitPrice: 301m, quoteBudget: null), Limit).ShouldBeTrue();
    }

    [Test]
    public void AMarketBuyIsValuedByItsBudget()
    {
        PerOrderLimit.Exceeds(Order(OrderSide.Buy, OrderType.Market, quantity: null, limitPrice: null, quoteBudget: 601m), Limit).ShouldBeTrue();
        PerOrderLimit.Exceeds(Order(OrderSide.Buy, OrderType.Market, quantity: null, limitPrice: null, quoteBudget: 600m), Limit).ShouldBeFalse();
    }

    [Test]
    public void ALimitBuyWithinTheLimitPasses()
    {
        PerOrderLimit.Exceeds(Order(OrderSide.Buy, OrderType.Limit, quantity: 1m, limitPrice: 568m, quoteBudget: null), Limit).ShouldBeFalse();
    }

    [Test]
    public void ASellAboveTheLimitPassesBecauseThePositionBoundsIt()
    {
        PerOrderLimit.Exceeds(Order(OrderSide.Sell, OrderType.Limit, quantity: 1m, limitPrice: 692m, quoteBudget: null), Limit).ShouldBeFalse();
        PerOrderLimit.Exceeds(Order(OrderSide.Sell, OrderType.Market, quantity: 10m, limitPrice: null, quoteBudget: null), Limit).ShouldBeFalse();
    }

    private static OrderSnapshot Order(OrderSide side, OrderType type, decimal? quantity, decimal? limitPrice, decimal? quoteBudget)
    {
        return new OrderSnapshot(
            Id: 1,
            Tag: null,
            side,
            type,
            quantity,
            limitPrice,
            quoteBudget,
            quantity ?? 0m,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.PendingSubmit);
    }
}