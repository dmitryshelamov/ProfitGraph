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
///     A candle of an open market without trades (ADR 0023, TC2) advances indicators but fills no order.
/// </summary>
public sealed class CandlesWithoutTradesTests
{
    private static readonly Timeframe Hour = Timeframe.Parse("1h");

    [Test]
    public void ACandleWithoutTrades_IsFlatWithoutVolume()
    {
        CandleValidator.IsValid(Quiet(index: 1, price: 105m), Hour).ShouldBeTrue();
        CandleValidator.IsValid(Quiet(index: 1, price: 105m) with { Volume = 1m }, Hour).ShouldBeFalse();
        CandleValidator.IsValid(Bar(index: 1, open: 100m, close: 105m) with { Volume = 0m, NoTrades = true }, Hour).ShouldBeFalse();
    }

    [Test]
    public void JsonOmitsTheFlagOfARealCandle_AndKeepsACandleWithoutTrades()
    {
        Candle quiet = Quiet(index: 1, price: 105m);

        JsonSerializer.Serialize(Bar(index: 1)).ShouldNotContain(nameof(Candle.NoTrades));
        JsonSerializer.Deserialize<Candle>(JsonSerializer.Serialize(quiet)).ShouldBe(quiet);
    }

    [Test]
    public void NoOrderFillsOnACandleWithoutTrades_AndTheReplayStaysDeterministic()
    {
        BacktestResult quiet = new BacktestEngine().Run(Ladder(flagged: true));
        BacktestResult again = new BacktestEngine().Run(Ladder(flagged: true));
        BacktestResult traded = new BacktestEngine().Run(Ladder(flagged: false));

        quiet.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        quiet.Ledger.ShouldBe(again.Ledger);
        BacktestLedgerEntry[] fills = [.. quiet.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled)];
        fills.ShouldNotBeEmpty("The ladder must fill for the comparison to mean anything.");
        fills.ShouldAllBe(entry => !entry.Context.Candle.NoTrades);
        traded.Ledger.ShouldContain(entry => entry.Kind == BacktestLedgerKind.Filled && entry.Context.Candle.Volume == 0m, "Flat candles with trades fill orders.");
        fills.ShouldAllBe(entry => entry.Context.Candle.Volume > 0m);
    }

    /// <summary>
    ///     Every third replay candle is flat at a price the ladder buys at; with <paramref name="flagged" /> it has no trades.
    /// </summary>
    private static BacktestInput Ladder(bool flagged)
    {
        CompiledTradingGraph graph = new TradingGraphCompiler()
            .Compile(LadderStrategyGraphGenerator.Generate(), Catalog)
            .Graph.ShouldNotBeNull();

        return new BacktestInput(
            graph,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6),
            new PreparedReplay(
                Hour,
                Enumerable.Range(start: 0, count: 10).Select(index => Bar(index)),
                Enumerable.Range(start: 10, count: 60)
                    .Select(index => index % 3 == 0
                        ? flagged ? Quiet(index, price: 85m) : Quiet(index, price: 85m) with { NoTrades = false }
                        : Bar(index, low: 85m, high: 110m, close: 105m))));
    }

    private static Candle Quiet(int index, decimal price)
    {
        return Bar(index, price) with { Volume = 0m, NoTrades = true };
    }
}