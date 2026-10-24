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

public sealed class VolumeMomentumTemplateTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    // Range candle 1 is the impulse: close 106 >= 100 × 1.05 and volume 30 >= 2 × 10. The entry fills at candle 2 open (106).
    [TestCase(117, 117, 117, 3, 117, TestName = "TargetExitsAtNextOpenAfterCloseReachesTarget")]
    [TestCase(100, 101, 101, 3, 101, TestName = "StopExitsAtNextOpenAfterCloseFallsToStop")]
    [TestCase(106, 106, 106, 4, 106, TestName = "TimeExitsAfterMaxHoldClosedCandles")]
    public void ExitsByTargetStopOrTime(decimal secondClose, decimal thirdOpen, decimal thirdClose, int exitCandle, decimal exitPrice)
    {
        Candle[] warmup = [Flat(index: 0, price: 100m), Flat(index: 1, price: 100m)];
        Candle[] range =
        [
            Flat(index: 2, price: 100m),
            Bar(index: 3, open: 100m, close: 106m, volume: 30m),
            Bar(index: 4, open: 106m, secondClose, volume: 10m),
            Bar(index: 5, thirdOpen, thirdClose, volume: 10m),
            Bar(index: 6, thirdClose, thirdClose, volume: 10m),
            Bar(index: 7, thirdClose, thirdClose, volume: 10m)
        ];
        BacktestResult result = Run(VolumeMomentumStrategyBlockFactory.Create(volumePeriod: 1, maxHold: 2), warmup, range);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Diagnostics.ShouldBeEmpty();
        BacktestLedgerEntry[] fills = result.Ledger
            .Where(entry => entry.Kind == BacktestLedgerKind.Filled)
            .ToArray();
        fills.Length.ShouldBe(2);
        fills[0].Fill!.Side.ShouldBe(OrderSide.Buy);
        fills[0].Fill!.Tag.ShouldBe("momentum.entry");
        fills[0].Fill!.Price.ShouldBe(106m);
        fills[0].Context.CandleIndex.ShouldBe(2);
        fills[0].Context.Phase.ShouldBe(PricePhase.Open);
        fills[1].Fill!.Side.ShouldBe(OrderSide.Sell);
        fills[1].Fill!.Tag.ShouldBe("momentum.exit");
        fills[1].Fill!.Price.ShouldBe(exitPrice);
        fills[1].Context.CandleIndex.ShouldBe(exitCandle);
        fills[1].Context.Phase.ShouldBe(PricePhase.Open);
    }

    [Test]
    public void ImpulseWithoutVolumeDoesNotEnter()
    {
        Candle[] warmup = [Flat(index: 0, price: 100m), Flat(index: 1, price: 100m)];
        Candle[] range = [Flat(index: 2, price: 100m), Bar(index: 3, open: 100m, close: 110m, volume: 19m), Flat(index: 4, price: 110m)];
        BacktestResult result = Run(VolumeMomentumStrategyBlockFactory.Create(volumePeriod: 1), warmup, range);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Ledger.ShouldNotContain(entry => entry.Kind == BacktestLedgerKind.Submitted);
    }

    private static BacktestResult Run(StrategyDefinition definition, IEnumerable<Candle> warmup, IEnumerable<Candle> range)
    {
        var input = new BacktestInput(
            Compile(definition),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 10000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.00000001m, priceTick: 0.00000001m, minQuantity: 0m, minNotional: 0m, basePrecision: 8, quotePrecision: 8),
            Timeframe.Parse("4h"),
            warmup,
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
        return Bar(index, price, price, volume: 10m);
    }

    private static Candle Bar(int index, decimal open, decimal close, decimal volume)
    {
        return new Candle(Start.AddHours(4 * index), open, Math.Max(open, close), Math.Min(open, close), close, volume);
    }
}