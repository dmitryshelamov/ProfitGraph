using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;
using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class MetricsOverflowTests
{
    [Test]
    public void ActualCompletedReplayPreservesExactMoneyWhenCapitalNormalizedPercentageDoesNotFit()
    {
        var graph = new StrategyGraph([Node("start", "OnStart"), Buy("buy", amount: 0.0000000000000000000000000001m)], [Wire("start", "buy", "trigger")]);
        Candle[] candles = new[] { Bar(index: 0, open: 0.0000000000000000000000000001m, close: 1m) };
        var observed = new List<CandleSnapshot>();
        BacktestResult result = Execute(graph, candles, capital: 0.0000000000000000000000000001m, quotePrecision: 28, observed);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Terminal.Equity.ShouldBe(1m);
        var metrics = new MetricsAccumulator(startingCapital: 0.0000000000000000000000000001m, new SampleRanges(InSampleBars: 1, OutOfSampleBars: 0));
        BacktestMetricsSnapshot summary = metrics.Advance(observed.Single(), newExecutions: 1).Snapshot;
        summary.Full.TotalPnl.ShouldBe(result.Terminal.TotalPnl);
        summary.Full.ReturnPercent.ShouldBeNull();
        summary.Full.RealizedPnl.ShouldBe(0m);
        summary.Full.UnrealizedPnl.ShouldBe(result.Terminal.UnrealizedPnl);
        summary.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "MetricsOverflow" && diagnostic.Message.Contains("Full.ReturnPercent"));
        metrics.ObservedCandles.ShouldBe(0);
    }

    [Test]
    public void ActualContinuousReplayOnlyMarksNonrepresentableOutOfSampleUnrealizedDeltaUnavailable()
    {
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"), Buy("first", amount: 1m), TradingNode("balance", "Balance"), Buy("rebuy", amount: null),
                TradingNode("sell", "MarketSell", new Dictionary<string, ParamValue> { ["tag"] = Text("sell") }, ActionEvaluationMode.OnCandleClose)
            ],
            [
                Wire("start", "first", "trigger"), Edge("balance", "baseAvailable", "sell", "quantity"),
                Edge("sell", "filled", "rebuy", "trigger"), Edge("balance", "quoteAvailable", "rebuy", "amount")
            ]);
        Candle[] candles = new[]
        {
            Bar(index: 0, open: 0.0000000000000000000000000001m, close: 6m),
            Bar(index: 1, open: 6m, close: 0.0000000000000000000000000001m)
        };
        var observed = new List<CandleSnapshot>();
        BacktestResult result = Execute(graph, candles, capital: 1m, quotePrecision: 0, observed);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Terminal.Equity.ShouldBe(1m);
        result.Ledger.Count(entry => entry.Fill != null).ShouldBe(3);
        observed[0].Equity.ShouldBe(60000000000000000000000000000m);
        observed[1].Account.RealizedPnl.ShouldBe(59999999999999999999999999999m);
        observed[1].UnrealizedPnl.ShouldBe(-59999999999999999999999999999m);
        var accumulator = new MetricsAccumulator(startingCapital: 1m, new SampleRanges(InSampleBars: 1, OutOfSampleBars: 1));
        accumulator = accumulator.Advance(observed[0], newExecutions: 1).Advance(observed[1], newExecutions: 2);
        SampleMetrics holdout = accumulator.Snapshot.OutOfSample.ShouldNotBeNull();
        holdout.BoundaryEquity.ShouldBe(60000000000000000000000000000m);
        holdout.EndEquity.ShouldBe(1m);
        holdout.TotalPnl.ShouldBe(-59999999999999999999999999999m);
        holdout.RealizedPnl.ShouldBe(59999999999999999999999999999m);
        holdout.UnrealizedPnl.ShouldBeNull();
        accumulator.Snapshot.Full.TotalPnl.ShouldBe(0m);
        accumulator.Snapshot.Full.TotalPnl.ShouldBe(accumulator.Snapshot.InSample.TotalPnl + holdout.TotalPnl);
        accumulator.Snapshot.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "MetricsOverflow" && diagnostic.Message.Contains("OutOfSample.UnrealizedPnl"));
    }

    [Test]
    public void FirstCloseLossUsesInitialCapitalPeakAndRejectsRepeatedOrFutureSnapshot()
    {
        var observed = new List<CandleSnapshot>();
        Execute(
            new StrategyGraph([Node("start", "OnStart"), Buy("buy", amount: 100m)], [Wire("start", "buy", "trigger")]),
            [Bar(index: 0, open: 100m, close: 80m)],
            capital: 100m,
            quotePrecision: 0,
            observed);
        var initial = new MetricsAccumulator(startingCapital: 100m, new SampleRanges(InSampleBars: 1, OutOfSampleBars: 0));
        MetricsAccumulator current = initial.Advance(observed.Single(), newExecutions: 1);
        current.Snapshot.Full.MaxDrawdownPercent.ShouldBe(20m);
        current.Snapshot.Full.TotalPnl.ShouldBe(-20m);
        current.Snapshot.Full.ReturnPercent.ShouldBe(-20m);
        Should.Throw<ArgumentException>(() => current.Advance(observed.Single(), newExecutions: 1));
        initial.Snapshot.Full.ObservedCandles.ShouldBe(0);
    }

    private static BacktestResult Execute(
        StrategyGraph graph,
        IEnumerable<Candle> candles,
        decimal capital,
        int quotePrecision,
        List<CandleSnapshot> observed)
    {
        TradingGraphCompilationResult compiled = new TradingGraphCompiler().Compile(graph, Catalog);
        compiled.Validation.Diagnostics.ShouldBeEmpty();
        var input = new BacktestInput(
            compiled.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(capital, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(
                quantityStep: 1m,
                priceTick: 0.0000000000000000000000000001m,
                minQuantity: 0m,
                minNotional: 0m,
                basePrecision: 0,
                quotePrecision),
            Timeframe.Parse("1h"),
            [],
            candles);
        var observer = Substitute.For<IBacktestObserver>();
        observer.When(value => value.OnCandleProcessed(Arg.Any<CandleSnapshot>())).Do(call => observed.Add(call.Arg<CandleSnapshot>()!));

        return new BacktestEngine().Run(input, observer);
    }
}