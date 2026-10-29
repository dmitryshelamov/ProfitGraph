using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

/// <summary>
///     Replays an indicator template on closes for its own warm-up plus a crafted range, with no fees or slippage.
/// </summary>
internal static class IndicatorTemplateReplay
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    public static CompiledTradingGraph Compile(StrategyDefinition definition)
    {
        new StrategyDefinitionValidator().Validate(definition, ProductionTradingNodeTests.Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, ProductionTradingNodeTests.Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }

    /// <summary>
    ///     The fills of a replay whose first candles are exactly the template's warm-up. A candle opens at the previous
    ///     close unless a gap gives its open; candle indexes in the ledger count from the first range candle.
    /// </summary>
    public static BacktestLedgerEntry[] Fills(StrategyDefinition definition, decimal[] closes, params (int Index, decimal Open)[] gaps)
    {
        CompiledTradingGraph graph = Compile(definition);
        Candle[] candles =
        [
            .. closes.Select((close, index) =>
            {
                decimal open = gaps.Any(gap => gap.Index == index) ? gaps.Single(gap => gap.Index == index).Open : closes[Math.Max(index - 1, val2: 0)];

                return new Candle(Start.AddHours(4 * index), open, Math.Max(open, close), Math.Min(open, close), close, Volume: 10m);
            })
        ];
        var input = new BacktestInput(
            graph,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 10000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.00000001m, priceTick: 0.00000001m, minQuantity: 0m, minNotional: 0m, basePrecision: 8, quotePrecision: 8),
            Timeframe.Parse("4h"),
            candles.Take(graph.WarmupCandles),
            candles.Skip(graph.WarmupCandles));
        BacktestResult result = new BacktestEngine().Run(input);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Diagnostics.ShouldBeEmpty();

        return
        [
            .. result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled)
        ];
    }

    /// <summary>
    ///     A market fill at the open of a range candle.
    /// </summary>
    public static void ShouldBeFill(this BacktestLedgerEntry entry, OrderSide side, string tag, int candleIndex, decimal price)
    {
        entry.Fill!.Side.ShouldBe(side);
        entry.Fill.Tag.ShouldBe(tag);
        entry.Fill.Price.ShouldBe(price);
        entry.Context.CandleIndex.ShouldBe(candleIndex);
        entry.Context.Phase.ShouldBe(PricePhase.Open);
    }
}