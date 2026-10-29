using System.Text.Json;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

/// <summary>
///     ADR 0057: on <see cref="LimitFillMode.Through" /> a resting limit fills only when the path trades strictly beyond it.
/// </summary>
public sealed class LimitFillModeTests
{
    [TestCase(OrderSide.Buy, false, 80, 0)]
    [TestCase(OrderSide.Buy, false, 81, 81)]
    [TestCase(OrderSide.Buy, false, 100, 100)]
    [TestCase(OrderSide.Buy, false, 101, 100)]
    [TestCase(OrderSide.Sell, true, 120, 0)]
    [TestCase(OrderSide.Sell, true, 119, 119)]
    [TestCase(OrderSide.Sell, true, 100, 100)]
    [TestCase(OrderSide.Sell, true, 99, 100)]
    public void Through_SelectsOnlyLevelsTheSegmentPassesBeyond(OrderSide side, bool ascending, int limit, int expectedPrice)
    {
        TradingAccount account = Account(LimitFillMode.Through);
        Order order = Submit(account, side, limit);

        OrderMatchCandidate? candidate = OrderMatcher.Next(Segment(ascending), currentPrice: 100m, account, candleIndex: 0);

        candidate.ShouldBe(expectedPrice == 0 ? null : new OrderMatchCandidate(order.Id, expectedPrice));
    }

    [TestCase(OrderSide.Buy, 100, null, false)]
    [TestCase(OrderSide.Buy, 101, null, true)]
    [TestCase(OrderSide.Buy, 100, 80, true)]
    [TestCase(OrderSide.Buy, 100, 120, false)]
    [TestCase(OrderSide.Sell, 100, null, false)]
    [TestCase(OrderSide.Sell, 99, null, true)]
    [TestCase(OrderSide.Sell, 100, 120, true)]
    [TestCase(OrderSide.Sell, 100, 80, false)]
    public void Through_AtAPointFillsTheLimitOnlyWhenThePathContinuesBeyondIt(OrderSide side, int limit, int? segmentEnd, bool executable)
    {
        TradingAccount account = Account(LimitFillMode.Through);
        Order order = Submit(account, side, limit);

        OrderMatchCandidate? candidate = OrderMatcher.NextAtPrice(price: 100m, account, candleIndex: 0, segmentEnd);

        candidate.ShouldBe(executable ? new OrderMatchCandidate(order.Id, pathPrice: 100m) : null);
    }

    [Test]
    public void Touch_KeepsTheInclusiveThresholds()
    {
        TradingAccount account = Account(LimitFillMode.Touch);
        Order buy = Submit(account, OrderSide.Buy, limit: 80m);

        OrderMatcher.Next(Segment(ascending: false), currentPrice: 100m, account, candleIndex: 0).ShouldBe(new OrderMatchCandidate(buy.Id, pathPrice: 80m));
    }

    [Test]
    public void LadderAverage_AtTheCandleLowFillsOnTouchAndWaitsOnThrough()
    {
        Candle[] candles = [Bar(index: 10, open: 100m, low: 90m, high: 104m, close: 100m), Bar(index: 11, open: 100m, low: 89.99m, high: 104m, close: 100m)];

        BacktestResult touch = Run(LimitFillMode.Touch, candles);
        BacktestResult through = Run(LimitFillMode.Through, candles);

        AverageFills(touch).ShouldBe([10]);
        AverageFills(through).ShouldBe([11]);
    }

    [Test]
    public void Through_IsDeterministicForIdenticalInputs()
    {
        Candle[] candles = Enumerable.Range(start: 10, count: 48)
            .Select(index => Bar(index, open: 100m, 90m - index % 3, 108m + index % 4, close: 100m))
            .ToArray();

        BacktestResult first = Run(LimitFillMode.Through, candles);
        BacktestResult second = Run(LimitFillMode.Through, candles);

        JsonSerializer.Serialize(second.Ledger).ShouldBe(JsonSerializer.Serialize(first.Ledger));
        JsonSerializer.Serialize(second.Terminal).ShouldBe(JsonSerializer.Serialize(first.Terminal));
        first.FillCount.ShouldBeGreaterThan(0);
    }

    private static int[] AverageFills(BacktestResult result)
    {
        return result.Ledger
            .Where(entry => entry is { Kind: BacktestLedgerKind.Filled, Fill.Side: OrderSide.Buy, Fill.Type: OrderType.Limit })
            .Select(entry => entry.Context.CandleIndex + 10)
            .ToArray();
    }

    private static BacktestResult Run(LimitFillMode mode, IReadOnlyList<Candle> candles)
    {
        CompiledTradingGraph graph = new TradingGraphCompiler()
            .Compile(LadderStrategyGraphGenerator.Generate(), Catalog)
            .Graph.ShouldNotBeNull();
        var replay = new PreparedReplay(Timeframe.Parse("1h"), Enumerable.Range(start: 0, count: 10).Select(index => Bar(index)), candles);
        var input = new BacktestInput(
            graph,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m).WithLimitFill(mode),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6),
            replay);

        return new BacktestEngine().Run(input);
    }

    private static TradingAccount Account(LimitFillMode mode)
    {
        ExecutionSettings settings = new ExecutionSettings(startingCapital: 100000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m).WithLimitFill(mode);
        var rules = new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 2, quotePrecision: 2);
        var account = new TradingAccount(settings, rules);
        OrderSubmission seed = account.Submit(
            new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 1000m, LimitPrice: null, Tag: null, "seed", EligibleFromCandleIndex: 0));
        account.Execute(seed.Order!.Id, referencePrice: 100m).IsFilled.ShouldBeTrue();

        return account;
    }

    private static Order Submit(TradingAccount account, OrderSide side, decimal limit)
    {
        OrderSubmission submission = account.Submit(
            new OrderRequest(side, OrderType.Limit, side == OrderSide.Buy ? limit : 1m, limit, Tag: null, "limit", EligibleFromCandleIndex: 0));
        submission.IsAccepted.ShouldBeTrue();

        return submission.Order!;
    }

    private static PriceSegment Segment(bool ascending)
    {
        return ascending
            ? new PriceSegment(start: 100m, end: 120m, PricePhase.LowToHigh)
            : new PriceSegment(start: 100m, end: 80m, PricePhase.OpenToLow);
    }
}