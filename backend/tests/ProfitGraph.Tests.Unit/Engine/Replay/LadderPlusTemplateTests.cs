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

public sealed class LadderPlusTemplateTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    // allocationCap = buyAmount leaves no room for averaging, so the position is the single entry at 100 with an exit at 110.
    [Test]
    public void StopSellsAtTheNextOpenAfterCloseFallsToStopAndStartsANewCycle()
    {
        Candle[] range =
        [
            Bar(index: 0, open: 100m, high: 100m, low: 100m, close: 100m), Bar(index: 1, open: 100m, high: 100m, low: 79m, close: 79m), Flat(index: 2, price: 79m)
        ];
        BacktestResult result = Run(LadderPlusStrategyBlockFactory.Create(allocationCap: 100m, stopLoss: 0.2m), range);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Diagnostics.ShouldBeEmpty();
        BacktestLedgerEntry[] fills = Fills(result);
        fills.Select(entry => (entry.Fill!.Tag!, entry.Fill.Side, entry.Fill.Price, entry.Context.CandleIndex))
            .ShouldBe(new[] { ("ladder.entry", OrderSide.Buy, 100m, 0), ("ladder.stop", OrderSide.Sell, 79m, 2), ("ladder.entry", OrderSide.Buy, 79m, 2) });
        result.Ledger.ShouldNotContain(entry => entry.Kind == BacktestLedgerKind.Filled && entry.Fill!.Tag == "ladder.exit");
    }

    [Test]
    public void CloseAboveStopKeepsThePosition()
    {
        Candle[] range =
        [
            Bar(index: 0, open: 100m, high: 100m, low: 100m, close: 100m), Bar(index: 1, open: 100m, high: 100m, low: 79m, close: 81m), Flat(index: 2, price: 81m)
        ];
        BacktestResult result = Run(LadderPlusStrategyBlockFactory.Create(allocationCap: 100m, stopLoss: 0.2m), range);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        Fills(result).Select(entry => entry.Fill!.Tag).ShouldBe(new[] { "ladder.entry" });
    }

    // The exit at 110 realizes 10 quote; a target at or below that stops the restart, a higher one re-enters at the exit price.
    [TestCase(arg1: 10, new[] { "ladder.entry", "ladder.exit" }, TestName = "ReachedProfitTargetBlocksTheNextCycle")]
    [TestCase(arg1: 11, new[] { "ladder.entry", "ladder.exit", "ladder.entry" }, TestName = "UnreachedProfitTargetStartsTheNextCycle")]
    public void ProfitTargetGatesNewCycles(decimal profitTarget, string[] tags)
    {
        Candle[] range = [Bar(index: 0, open: 100m, high: 100m, low: 100m, close: 100m), Bar(index: 1, open: 100m, high: 111m, low: 100m, close: 105m)];
        BacktestResult result = Run(LadderPlusStrategyBlockFactory.Create(allocationCap: 100m, profitTarget: profitTarget), range);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        Fills(result).Select(entry => entry.Fill!.Tag).ShouldBe(tags);
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

    private static Candle Flat(int index, decimal price)
    {
        return Bar(index, price, price, price, price);
    }

    private static Candle Bar(int index, decimal open, decimal high, decimal low, decimal close)
    {
        return new Candle(Start.AddHours(4 * index), open, high, low, close, Volume: 10m);
    }
}