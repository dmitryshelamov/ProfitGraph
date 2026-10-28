using ProfitGraph.Domain.Optimization.WalkForward;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Domain.Optimization.WalkForward;

[TestFixture]
internal sealed class WalkForwardEquityObserverTests
{
    [TestCase(1)]
    [TestCase(7)]
    [TestCase(2000)]
    public void ShortRangesKeepEveryExactCloseAtAbsoluteReplayIndex(int count)
    {
        var observer = new WalkForwardEquityObserver(startingCapital: 1000m, new SampleRanges(count, OutOfSampleBars: 0), testStart: 17, Candles(count));

        ObserveCloses(observer, count);

        observer.Equity.Count.ShouldBe(count);

        for (var index = 0; index < count; index++)
        {
            observer.Equity[index].ShouldBe(new WalkForwardEquityPoint(17 + index, 1000m + index / 10m, DateTimeOffset.UnixEpoch.AddHours(index)));
        }

        ((ICollection<WalkForwardEquityPoint>)observer.Equity).IsReadOnly.ShouldBeTrue();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EngineMetricsAndCloseEquityMatchTheExistingMetricsPath(bool ladder)
    {
        StrategyGraph graph = ladder ? LadderStrategyGraphGenerator.Generate() : SmaStrategyGraphGenerator.Generate();
        INodeDefinition[] definitions = EngineGraphFixture.Definitions
            .Concat(Enum.GetValues<TradingNodeBehavior>().Select(behavior => new TradingNodeDefinition(behavior)))
            .ToArray();
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, definitions);
        decimal[] prices = [4m, 3m, 2m, 5m, 6m, 2m, 1m, 4m, 6m, 2m];
        Candle[] candles = prices
            .Select((price, index) => ladder
                ? new Candle(DateTimeOffset.UnixEpoch.AddHours(index), Open: 100m, High: 110m, Low: 85m, Close: 105m, Volume: 1m)
                : new Candle(DateTimeOffset.UnixEpoch.AddHours(index), price, price, price, price, Volume: 1m))
            .ToArray();
        var input = new BacktestInput(
            compilation.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            candles,
            OutOfSampleSplit.None);
        var metrics = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        var equity = new WalkForwardEquityObserver(input.Settings.StartingCapital, input.SampleRanges, testStart: 72, candles);
        var engine = new BacktestEngine();
        BacktestResult baseline = engine.Run(input, metrics);
        BacktestResult captured = engine.Run(input, equity);

        baseline.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        captured.Terminal.Outcome.ShouldBe(baseline.Terminal.Outcome);
        captured.Terminal.ProcessedCandles.ShouldBe(baseline.Terminal.ProcessedCandles);
        captured.Terminal.Context.ShouldBe(baseline.Terminal.Context);
        captured.Terminal.Equity.ShouldBe(baseline.Terminal.Equity);
        captured.Terminal.UnrealizedPnl.ShouldBe(baseline.Terminal.UnrealizedPnl);
        captured.Terminal.TotalPnl.ShouldBe(baseline.Terminal.TotalPnl);
        captured.Terminal.Diagnostic.ShouldBe(baseline.Terminal.Diagnostic);
        equity.Snapshot.Full.ShouldBe(metrics.Snapshot.Full);
        equity.Snapshot.InSample.ShouldBe(metrics.Snapshot.InSample);
        equity.Snapshot.OutOfSample.ShouldBe(metrics.Snapshot.OutOfSample);
        equity.Snapshot.Diagnostics.ShouldBe(metrics.Snapshot.Diagnostics);
        equity.Snapshot.Full.Executions.ShouldBeGreaterThan(0);
        equity.Equity.Count.ShouldBe(candles.Length);
        equity.Equity[0].CandleIndex.ShouldBe(72);
        equity.Equity[^1].ShouldBe(new WalkForwardEquityPoint(CandleIndex: 81, metrics.Snapshot.Full.EndEquity.ShouldNotBeNull(), candles[^1].OpenTime));
    }

    private static void ObserveCloses(WalkForwardEquityObserver observer, int count)
    {
        for (var index = 0; index < count; index++)
        {
            observer.OnCandleMetrics(
                new CandleMetricObservation(
                    index + 1L,
                    index,
                    StartingCapital: 1000m,
                    1000m + index / 10m,
                    index / 10m,
                    RealizedPnl: 0m,
                    FeesBase: 0m,
                    FeesQuote: 0m));
        }
    }

    private static IReadOnlyList<Candle> Candles(int count)
    {
        return Enumerable.Range(start: 0, count)
            .Select(index => new Candle(DateTimeOffset.UnixEpoch.AddHours(index), Open: 1m, High: 1m, Low: 1m, Close: 1m, Volume: 1m))
            .ToArray();
    }

    [Test]
    public void EquityUsesActualCandleTimesAcrossHistoryGaps()
    {
        Candle[] candles = Candles(count: 3).ToArray();
        candles[2] = candles[2] with { OpenTime = DateTimeOffset.UnixEpoch.AddHours(8) };
        var observer = new WalkForwardEquityObserver(startingCapital: 1000m, new SampleRanges(InSampleBars: 3, OutOfSampleBars: 0), testStart: 20, candles);

        ObserveCloses(observer, count: 3);

        observer.Equity.Select(point => point.Time).ShouldBe(candles.Select(candle => (DateTimeOffset?)candle.OpenTime));
        observer.Equity[^1].CandleIndex.ShouldBe(22);
    }

    [Test]
    public void RejectedMetricObservationCannotAppendAnEquityPoint()
    {
        var observer = new WalkForwardEquityObserver(startingCapital: 1000m, new SampleRanges(InSampleBars: 1, OutOfSampleBars: 0), testStart: 0, Candles(count: 1));
        var invalid = new CandleMetricObservation(
            Sequence: 1,
            CandleIndex: 0,
            StartingCapital: 500m,
            Equity: 500m,
            UnrealizedPnl: 0m,
            RealizedPnl: 0m,
            FeesBase: 0m,
            FeesQuote: 0m);

        Should.Throw<ArgumentException>(() => observer.OnCandleMetrics(invalid));
        observer.Equity.ShouldBeEmpty();
    }

    [Test]
    public void RejectsAbsoluteRangeBeyondTheReplayIndexLimit()
    {
        Should.Throw<ArgumentException>(() => new WalkForwardEquityObserver(
            startingCapital: 1000m,
            new SampleRanges(InSampleBars: 2, OutOfSampleBars: 0),
            int.MaxValue,
            Candles(count: 2)));
    }

    [Test]
    public void RemainderIsDistributedAcrossBucketsInsteadOfAppendedToTheFinalBucket()
    {
        var observer = new WalkForwardEquityObserver(
            startingCapital: 1000m,
            new SampleRanges(InSampleBars: 10003, OutOfSampleBars: 0),
            testStart: 17000,
            Candles(count: 10003));

        ObserveCloses(observer, count: 10003);

        observer.Equity.Count.ShouldBe(2000);
        observer.Equity[0].ShouldBe(new WalkForwardEquityPoint(CandleIndex: 17004, CloseEquity: 1000.4m, DateTimeOffset.UnixEpoch.AddHours(4)));
        observer.Equity[665].CandleIndex.ShouldBe(20329);
        observer.Equity[666].CandleIndex.ShouldBe(20335);
        observer.Equity[1333].CandleIndex.ShouldBe(23671);
        observer.Equity[1999].ShouldBe(new WalkForwardEquityPoint(CandleIndex: 27002, CloseEquity: 2000.2m, DateTimeOffset.UnixEpoch.AddHours(10002)));
    }

    [Test]
    public void TwoThousandAndOneClosesUseBalancedBucketsAndTheTrueFinalClose()
    {
        var observer = new WalkForwardEquityObserver(
            startingCapital: 1000m,
            new SampleRanges(InSampleBars: 2001, OutOfSampleBars: 0),
            testStart: 300,
            Candles(count: 2001));

        ObserveCloses(observer, count: 2001);

        observer.Equity.Count.ShouldBe(2000);
        observer.Equity[0].ShouldBe(new WalkForwardEquityPoint(CandleIndex: 300, CloseEquity: 1000m, DateTimeOffset.UnixEpoch));
        observer.Equity[1998].ShouldBe(new WalkForwardEquityPoint(CandleIndex: 2298, CloseEquity: 1199.8m, DateTimeOffset.UnixEpoch.AddHours(1998)));
        observer.Equity[1999].ShouldBe(new WalkForwardEquityPoint(CandleIndex: 2300, CloseEquity: 1200m, DateTimeOffset.UnixEpoch.AddHours(2000)));
    }
}