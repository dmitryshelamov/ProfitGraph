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

/// <summary>
///     The level trade of a scanner trade card (ADR 0060): a limit entry, a limit target and a stop on the close, once per run.
///     Entry 100, stop 95, target 110 throughout.
/// </summary>
public sealed class LevelTradeTemplateTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void TheTargetSellsWhatTheEntryBoughtAndTheTradeDoesNotRepeat()
    {
        Candle[] range =
        [
            Flat(index: 0, price: 102m), Bar(index: 1, open: 102m, high: 102m, low: 99m, close: 101m), Bar(index: 2, open: 101m, high: 111m, low: 101m, close: 108m),
            Bar(index: 3, open: 108m, high: 108m, low: 99m, close: 100m), Flat(index: 4, price: 100m)
        ];

        BacktestResult result = Run(range);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Diagnostics.ShouldBeEmpty();
        Fills(result).ShouldBe([(LevelTradeStrategyBlockFactory.EntryTag, OrderSide.Buy, 100m), (LevelTradeStrategyBlockFactory.TargetTag, OrderSide.Sell, 110m)]);
    }

    [Test]
    public void ACloseAtTheStopSellsAtTheNextOpenAndCancelsTheTarget()
    {
        Candle[] range =
        [
            Flat(index: 0, price: 102m), Bar(index: 1, open: 102m, high: 102m, low: 99m, close: 101m), Bar(index: 2, open: 101m, high: 101m, low: 94m, close: 95m),
            Bar(index: 3, open: 94m, high: 94m, low: 93m, close: 93m), Bar(index: 4, open: 93m, high: 112m, low: 93m, close: 99m), Flat(index: 5, price: 99m)
        ];

        BacktestResult result = Run(range);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        Fills(result).ShouldBe([(LevelTradeStrategyBlockFactory.EntryTag, OrderSide.Buy, 100m), (LevelTradeStrategyBlockFactory.StopTag, OrderSide.Sell, 94m)]);
    }

    [Test]
    public void ALowBelowTheStopWithACloseAboveItKeepsThePosition()
    {
        Candle[] range =
        [
            Flat(index: 0, price: 102m), Bar(index: 1, open: 102m, high: 102m, low: 99m, close: 101m), Bar(index: 2, open: 101m, high: 101m, low: 90m, close: 96m),
            Flat(index: 3, price: 96m)
        ];

        Fills(Run(range)).ShouldBe([(LevelTradeStrategyBlockFactory.EntryTag, OrderSide.Buy, 100m)]);
    }

    [Test]
    public void AnUnreachedEntryTradesNothing()
    {
        Candle[] range = [Flat(index: 0, price: 102m), Bar(index: 1, open: 102m, high: 115m, low: 101m, close: 112m), Flat(index: 2, price: 105m)];

        Fills(Run(range)).ShouldBeEmpty();
    }

    private static (string Tag, OrderSide Side, decimal Price)[] Fills(BacktestResult result)
    {
        return
        [
            .. result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled)
                .Select(entry => (entry.Fill!.Tag!, entry.Fill.Side, entry.Fill.Price))
        ];
    }

    private static BacktestResult Run(IEnumerable<Candle> range)
    {
        var input = new BacktestInput(
            Compile(LevelTradeStrategyBlockFactory.Create()),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 10000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.00000001m, priceTick: 0.00000001m, minQuantity: 0m, minNotional: 0m, basePrecision: 8, quotePrecision: 8),
            Timeframe.Parse("1m"),
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
        return new Candle(Start.AddMinutes(index), open, high, low, close, Volume: 10m);
    }
}