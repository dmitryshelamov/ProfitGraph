using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class ProfitFoxDcaTemplateTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    // The start buy fills at the first open; a close where barsHeld is a multiple of the interval buys at the next open.
    [TestCase(arg1: 1, new[] { 0, 1, 2, 3, 4, 5, 6 }, TestName = "EveryClosedCandleBuysAtTheNextOpen")]
    [TestCase(arg1: 3, new[] { 0, 3, 6 }, TestName = "EveryThirdClosedCandleBuysAtTheNextOpen")]
    public void BuysAtStartAndAfterEveryInterval(int interval, int[] candles)
    {
        BacktestResult result = Run(ProfitFoxDcaStrategyBlockFactory.Create(allocationCap: 10000m, interval: interval), Flat(count: 7, price: 100m));
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Diagnostics.ShouldBeEmpty();
        BacktestLedgerEntry[] fills = Fills(result);
        fills.Select(entry => entry.Context.CandleIndex).ShouldBe(candles);
        fills.ShouldAllBe(entry => entry.Fill!.Side == OrderSide.Buy && entry.Fill.Tag == "dca.buy" && entry.Context.Phase == PricePhase.Open);
    }

    [Test]
    public void StopsBuyingWhenTheNextBuyWouldExceedTheAllocationCap()
    {
        BacktestResult result = Run(ProfitFoxDcaStrategyBlockFactory.Create(allocationCap: 300m), Flat(count: 6, price: 100m));
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        Fills(result).Select(entry => entry.Context.CandleIndex).ShouldBe(new[] { 0, 1, 2 });
        result.Ledger.ShouldNotContain(entry => entry.Kind == BacktestLedgerKind.Rejected);
    }

    private static BacktestLedgerEntry[] Fills(BacktestResult result)
    {
        return result.Ledger
            .Where(entry => entry.Kind == BacktestLedgerKind.Filled)
            .ToArray();
    }

    private static BacktestResult Run(StrategyDefinition definition, IEnumerable<Candle> range)
    {
        var input = new BacktestInput(
            Compile(definition),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 10000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.00000001m, priceTick: 0.00000001m, minQuantity: 0m, minNotional: 0m, basePrecision: 8, quotePrecision: 8),
            Timeframe.Parse("4h"),
            [],
            range);

        return new BacktestEngine().Run(input);
    }

    private static CompiledTradingGraph Compile(StrategyDefinition definition)
    {
        new StrategyDefinitionValidator().Validate(definition, ProductionTradingNodeTests.Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, ProductionTradingNodeTests.Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }

    private static IEnumerable<Candle> Flat(int count, decimal price)
    {
        return Enumerable.Range(start: 0, count)
            .Select(index => new Candle(Start.AddHours(4 * index), price, price, price, price, Volume: 10m));
    }
}