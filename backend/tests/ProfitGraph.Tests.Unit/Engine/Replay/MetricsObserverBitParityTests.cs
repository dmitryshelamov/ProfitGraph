using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

[TestFixture]
internal sealed class MetricsObserverBitParityTests
{
    [TestCase(arg: false)]
    [TestCase(arg: true)]
    public void MetricsOnlyPreservesEveryDecimalBitAcrossContinuousHoldoutAndEarlierSnapshots(bool ladder)
    {
        StrategyDefinition definition = ladder
            ? LadderStrategyBlockFactory.Create()
            : SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 3);
        CompiledTradingGraph graph = new TradingGraphCompiler()
            .Compile(new StrategyDefinitionLowerer(Catalog).Lower(definition).Graph, Catalog)
            .Graph.ShouldNotBeNull();
        decimal[] prices = [4.00m, 3.00m, 2.00m, 5.00m, 6.00m, 2.00m, 1.00m, 4.00m, 6.00m, 2.00m, 5.00m, 6.00m];
        Candle[] candles = prices
            .Select((price, index) => ladder
                ? new Candle(DateTimeOffset.UnixEpoch.AddHours(index), Open: 100.00m, High: 110.00m, Low: 85.00m, Close: 105.00m, Volume: 1m)
                : new Candle(DateTimeOffset.UnixEpoch.AddHours(index), price, price, price, price, Volume: 1m))
            .ToArray();
        var input = new BacktestInput(
            graph,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000.00m, slippage: 0m, limitFeeRate: 0.0010m, marketFeeRate: 0.0010m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            candles,
            OutOfSampleSplit.Create(fraction: 0.4m));
        var reference = new MetricsAccumulator(input.Settings.StartingCapital, input.SampleRanges);
        var scalar = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        var earlierSnapshots = new List<(BacktestMetricsSnapshot Snapshot, int[][] Bits)>();
        var executions = 0;
        var observer = Substitute.For<IBacktestObserver>();
        observer.When(value => value.OnLedger(Arg.Any<BacktestLedgerEntry>()))
            .Do(call =>
            {
                var entry = call.Arg<BacktestLedgerEntry>()!;
                scalar.OnLedger(entry);

                if (entry.Kind == BacktestLedgerKind.Filled)
                {
                    executions++;
                }
            });
        observer.When(value => value.OnCandleProcessed(Arg.Any<CandleSnapshot>()))
            .Do(call =>
            {
                var candle = call.Arg<CandleSnapshot>()!;
                reference = reference.Advance(candle, executions);
                executions = 0;
                scalar.OnCandleMetrics(
                    new CandleMetricObservation(
                        candle.Sequence,
                        candle.Context.CandleIndex,
                        candle.Account.StartingCapital,
                        candle.Equity,
                        candle.UnrealizedPnl,
                        candle.Account.RealizedPnl,
                        candle.Account.FeesBase,
                        candle.Account.FeesQuote));
                AssertSnapshot(scalar.Snapshot, reference.Snapshot);
                earlierSnapshots.Add((scalar.Snapshot, Bits(scalar.Snapshot)));
            });
        var engine = new BacktestEngine();
        BacktestResult retained = engine.Run(input, observer, options: new BacktestRunOptions { RetainDispatchProjections = true });
        var metricsOnly = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        BacktestResult trial = engine.Run(input, metricsOnly, options: new BacktestRunOptions { RetainLedger = false });
        retained.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        trial.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        retained.FillCount.ShouldBeGreaterThan(0);
        trial.FillCount.ShouldBe(retained.FillCount);
        AssertSnapshot(metricsOnly.Snapshot, reference.Snapshot);
        reference.Snapshot.InSample.Executions.ShouldBeGreaterThan(0);
        reference.Snapshot.OutOfSample.ShouldNotBeNull().Executions.ShouldBeGreaterThan(0);
        earlierSnapshots.Count.ShouldBe(candles.Length);

        foreach ((BacktestMetricsSnapshot snapshot, int[][] bits) in earlierSnapshots)
        {
            AssertBits(Bits(snapshot), bits);
        }
    }

    private static void AssertSnapshot(BacktestMetricsSnapshot actual, BacktestMetricsSnapshot expected)
    {
        AssertSegment(actual.Full, expected.Full);
        AssertSegment(actual.InSample, expected.InSample);
        AssertSegment(actual.OutOfSample.ShouldNotBeNull(), expected.OutOfSample.ShouldNotBeNull());
        actual.Diagnostics.ShouldBe(expected.Diagnostics);
        AssertBits(Bits(actual), Bits(expected));
    }

    private static void AssertSegment(SampleMetrics actual, SampleMetrics expected)
    {
        actual.StartIndex.ShouldBe(expected.StartIndex);
        actual.EndExclusive.ShouldBe(expected.EndExclusive);
        actual.ObservedCandles.ShouldBe(expected.ObservedCandles);
        actual.Executions.ShouldBe(expected.Executions);
    }

    private static int[][] Bits(BacktestMetricsSnapshot snapshot)
    {
        var result = new List<int[]>();

        foreach (SampleMetrics segment in new[] { snapshot.Full, snapshot.InSample, snapshot.OutOfSample.ShouldNotBeNull() })
        {
            decimal?[] values =
            [
                segment.BoundaryEquity, segment.EndEquity, segment.TotalPnl, segment.RealizedPnl, segment.UnrealizedPnl,
                segment.ReturnPercent, segment.FeesBase, segment.FeesQuote, segment.MaxDrawdownPercent
            ];

            foreach (decimal? value in values)
            {
                result.Add(value.HasValue ? decimal.GetBits(value.Value) : Array.Empty<int>());
            }
        }

        return result.ToArray();
    }

    private static void AssertBits(int[][] actual, int[][] expected)
    {
        actual.Length.ShouldBe(expected.Length);

        for (var index = 0; index < actual.Length; index++)
        {
            actual[index].ShouldBe(expected[index]);
        }
    }
}