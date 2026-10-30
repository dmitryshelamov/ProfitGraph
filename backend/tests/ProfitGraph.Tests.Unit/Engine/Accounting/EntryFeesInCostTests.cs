using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

/// <summary>
///     The entry fees a position's cost basis holds (ADR 0063): a buy's base fee valued at its fill's price, kept through a
///     checkpoint and leaving with the cost basis in proportion to what is sold. The cost basis and the realized profit are those
///     strategies see.
/// </summary>
public sealed class EntryFeesInCostTests
{
    private static readonly ExecutionSettings Settings = new ExecutionSettings(startingCapital: 100m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m);

    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.01m,
        priceTick: 0.01m,
        minQuantity: 0.01m,
        minNotional: 1m,
        basePrecision: 2,
        quotePrecision: 4);

    [Test]
    public void ABaseFeeOfABuyStaysInTheCostAndLeavesWithWhatIsSold()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        Order buy = account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 25m, LimitPrice: 5m, "entry", "buy", EligibleFromCandleIndex: 1))
            .Order.ShouldNotBeNull();
        account.ApplyExternalUpdate(ExternalOrderUpdate.Accept(buy.Id), rejectionAttemptId: 1);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy.Id, grossBase: 5m, grossQuote: 25m, fee: 0.05m), rejectionAttemptId: 1);

        ExternalAccountCheckpoint bought = account.CaptureCheckpoint();
        bought.BaseTotal.ShouldBe(4.95m);
        bought.CostBasis.ShouldBe(25m);
        bought.FeesInCost.ShouldBe(0.25m);
        bought.RealizedPnl.ShouldBe(0m);

        ExternallyExecutedAccount restored = ExternallyExecutedAccount.Restore(bought, Settings, Rules);
        Order half = restored.Submit(new OrderRequest(OrderSide.Sell, OrderType.Limit, Amount: 2.47m, LimitPrice: 6m, "exit", "sell", EligibleFromCandleIndex: 2))
            .Order.ShouldNotBeNull();
        restored.ApplyExternalUpdate(ExternalOrderUpdate.Accept(half.Id), rejectionAttemptId: 1);
        restored.ApplyExternalUpdate(ExternalOrderUpdate.Final(half.Id, grossBase: 2.47m, grossQuote: 14.82m, fee: 0.0148m), rejectionAttemptId: 1);

        ExternalAccountCheckpoint partial = restored.CaptureCheckpoint();
        partial.FeesInCost.ShouldBe(0.25m - 0.25m * 2.47m / 4.95m);
        partial.CostBasis.ShouldBe(25m - 25m * 2.47m / 4.95m);

        Order rest = restored.Submit(new OrderRequest(OrderSide.Sell, OrderType.Limit, Amount: 2.48m, LimitPrice: 6m, "rest", "sell", EligibleFromCandleIndex: 3))
            .Order.ShouldNotBeNull();
        restored.ApplyExternalUpdate(ExternalOrderUpdate.Accept(rest.Id), rejectionAttemptId: 1);
        restored.ApplyExternalUpdate(ExternalOrderUpdate.Final(rest.Id, grossBase: 2.48m, grossQuote: 14.88m, fee: 0.0149m), rejectionAttemptId: 1);

        ExternalAccountCheckpoint flat = restored.CaptureCheckpoint();
        flat.BaseTotal.ShouldBe(0m);
        flat.FeesInCost.ShouldBe(0m);
        flat.CostBasis.ShouldBe(0m);

        // 14.82 − 0.0148 + 14.88 − 0.0149 − 25: both sells' fees and the buy's fee, held in the cost until the position closed.
        flat.RealizedPnl.ShouldBe(4.6703m);
    }

    [Test]
    public void AQuoteFeeOfABuyIsHeldAsItIs()
    {
        var rules = new InstrumentRules(
            quantityStep: 1m,
            priceTick: 0.01m,
            minQuantity: 1m,
            minNotional: 1m,
            basePrecision: 0,
            quotePrecision: 2,
            FeeAsset.Quote);
        var account = new ExternallyExecutedAccount(Settings, rules);
        Order buy = account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 50m, LimitPrice: 10m, "entry", "buy", EligibleFromCandleIndex: 1))
            .Order.ShouldNotBeNull();
        account.ApplyExternalUpdate(ExternalOrderUpdate.Accept(buy.Id), rejectionAttemptId: 1);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy.Id, grossBase: 4m, grossQuote: 40m, fee: 0.04m), rejectionAttemptId: 1);

        ExternalAccountCheckpoint bought = account.CaptureCheckpoint();
        bought.CostBasis.ShouldBe(40.04m);
        bought.FeesInCost.ShouldBe(0.04m);
    }
}