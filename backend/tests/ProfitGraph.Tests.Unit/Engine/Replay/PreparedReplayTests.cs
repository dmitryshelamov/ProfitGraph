using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class PreparedReplayTests
{
    [Test]
    public void OwnedHistoryProducesTheSameReplayAndWindowFactsAsCopiedPartitions()
    {
        List<Candle> candles = Enumerable.Range(start: 0, count: 10).Select(index => Bar(index)).ToList();
        IReadOnlyList<Candle> owned = candles.AsReadOnly();
        Timeframe timeframe = Timeframe.Parse("1h");
        PreparedReplay shared = PreparedReplay.FromOwnedHistory(timeframe, owned, warmupCount: 3);
        var copied = new PreparedReplay(timeframe, candles.Take(3), candles.Skip(3));

        shared.Warmup.ShouldBe(copied.Warmup);
        shared.Replay.ShouldBe(copied.Replay);
        shared.Slice(5).Window(start: 2, count: 3).Warmup.ShouldBe(copied.Slice(5).Window(start: 2, count: 3).Warmup);
        shared.Slice(5).Window(start: 2, count: 3).Replay.ShouldBe(copied.Slice(5).Window(start: 2, count: 3).Replay);
        Should.Throw<ArgumentOutOfRangeException>(() => PreparedReplay.FromOwnedHistory(timeframe, owned, owned.Count));
    }

    [Test]
    public void FreezesSourceArraysAndSharesImmutableWarmupAndCandleObjectsAcrossSlices()
    {
        Candle[] warmup = [Bar(0)];
        Candle[] replay = [Bar(1), Bar(2), Bar(3)];
        Candle firstReplay = replay[0];
        var prepared = new PreparedReplay(Timeframe.Parse("1h"), warmup, replay);
        warmup[0] = Bar(20);
        replay[0] = Bar(21);
        PreparedReplay slice = prepared.Slice(2);

        prepared.Warmup[0].OpenTime.ShouldBe(DateTimeOffset.UnixEpoch);
        slice.Warmup.ShouldBeSameAs(prepared.Warmup);
        slice.Replay[0].ShouldBeSameAs(firstReplay);
        slice.Replay.Count.ShouldBe(2);
        slice.Slice(1).Replay.ShouldBe(new[] { firstReplay });
        prepared.Slice(3).ShouldBeSameAs(prepared);
        Should.Throw<ArgumentOutOfRangeException>(() => _ = slice.Replay[2]);
        Should.Throw<NotSupportedException>(() => ((IList<Candle>)prepared.Replay)[0] = Bar(99));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(4)]
    public void RejectsEmptyOrOutOfBoundsPrefixes(int count)
    {
        var prepared = new PreparedReplay(Timeframe.Parse("1h"), Array.Empty<Candle>(), [Bar(0), Bar(1), Bar(2)]);

        Should.Throw<ArgumentOutOfRangeException>(() => prepared.Slice(count));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(5)]
    public void WindowsShareOriginalCandlesAndUseOnlyImmediatelyPrecedingWarmup(int start)
    {
        Candle[] candles = Enumerable.Range(start: 0, count: 10)
            .Select(index => Bar(index))
            .ToArray();
        var prepared = new PreparedReplay(Timeframe.Parse("1h"), candles.Take(3), candles.Skip(3));
        PreparedReplay window = prepared.Window(start, count: 2);
        Candle[] expectedWarmup = candles.Skip(start).Take(3).ToArray();
        Candle[] expectedReplay = candles.Skip(3 + start).Take(2).ToArray();

        window.Warmup.ShouldBe(expectedWarmup);
        window.Replay.ShouldBe(expectedReplay);
        window.Timeframe.ShouldBe(prepared.Timeframe);
        window.Warmup.ShouldNotBeOfType<Candle[]>();
        window.Replay.ShouldNotBeOfType<Candle[]>();
        (window.Warmup is IList<Candle> mutableWarmup && !mutableWarmup.IsReadOnly).ShouldBeFalse();
        (window.Replay is IList<Candle> mutableReplay && !mutableReplay.IsReadOnly).ShouldBeFalse();

        for (var index = 0; index < window.Warmup.Count; index++)
        {
            window.Warmup[index].ShouldBeSameAs(candles[start + index]);
            window.Warmup[index].OpenTime.ShouldBeLessThan(window.Replay[0].OpenTime);
        }

        for (var index = 0; index < window.Replay.Count; index++)
        {
            window.Replay[index].ShouldBeSameAs(candles[3 + start + index]);
        }

        Should.Throw<ArgumentOutOfRangeException>(() => _ = window.Warmup[-1]);
        Should.Throw<ArgumentOutOfRangeException>(() => _ = window.Warmup[window.Warmup.Count]);
        Should.Throw<ArgumentOutOfRangeException>(() => _ = window.Replay[-1]);
        Should.Throw<ArgumentOutOfRangeException>(() => _ = window.Replay[window.Replay.Count]);
    }

    [Test]
    public void WindowsAndSlicesPreserveRangesWhenComposed()
    {
        Candle[] candles = Enumerable.Range(start: 0, count: 12)
            .Select(index => Bar(index))
            .ToArray();
        var prepared = new PreparedReplay(Timeframe.Parse("1h"), candles.Take(3), candles.Skip(3));
        PreparedReplay window = prepared.Slice(8).Window(start: 2, count: 6).Slice(5).Window(start: 2, count: 3);

        window.Warmup.ShouldBe(candles.Skip(4).Take(3));
        window.Replay.ShouldBe(candles.Skip(7).Take(3));
        window.Slice(2).Warmup.ShouldBeSameAs(window.Warmup);
        window.Window(start: 0, count: 3).ShouldBeSameAs(window);
        window.Window(start: 0, count: 2).Warmup.ShouldBeSameAs(window.Warmup);
        window.Window(start: 0, count: 2).Replay.ShouldBe(window.Slice(2).Replay);
        Should.Throw<ArgumentOutOfRangeException>(() => window.Window(start: 2, count: 2));
    }

    [Test]
    public void WindowsWithNoOriginalWarmupKeepWarmupEmpty()
    {
        var prepared = new PreparedReplay(Timeframe.Parse("1h"), Array.Empty<Candle>(), [Bar(0), Bar(1), Bar(2)]);
        PreparedReplay window = prepared.Window(start: 2, count: 1);

        window.Warmup.ShouldBeEmpty();
        window.Replay[0].ShouldBeSameAs(prepared.Replay[2]);
    }

    [TestCase(arg1: -1, arg2: 1)]
    [TestCase(arg1: 3, arg2: 1)]
    [TestCase(int.MaxValue, arg2: 1)]
    [TestCase(arg1: 0, arg2: 0)]
    [TestCase(arg1: 0, arg2: -1)]
    [TestCase(arg1: 1, arg2: 3)]
    [TestCase(arg1: 1, int.MaxValue)]
    public void RejectsEmptyOrOutOfBoundsWindowsWithoutIndexOverflow(int start, int count)
    {
        var prepared = new PreparedReplay(Timeframe.Parse("1h"), Array.Empty<Candle>(), [Bar(0), Bar(1), Bar(2)]);

        Should.Throw<ArgumentOutOfRangeException>(() => prepared.Window(start, count));
    }

    [TestCase(arg1: false, arg2: 0)]
    [TestCase(arg1: false, arg2: 1)]
    [TestCase(arg1: false, arg2: 5)]
    [TestCase(arg1: true, arg2: 0)]
    [TestCase(arg1: true, arg2: 1)]
    [TestCase(arg1: true, arg2: 5)]
    public void WindowMetricsExactlyEqualReplayBuiltFromCopiedCandles(bool ladder, int start)
    {
        StrategyGraph graph = ladder ? LadderStrategyGraphGenerator.Generate() : SmaStrategyGraphGenerator.Generate();
        decimal[] prices = [4m, 3m, 2m, 5m, 6m, 2m, 1m, 4m, 6m, 2m];
        Candle[] candles = Enumerable.Range(start: 0, count: 30)
            .Select(index => ladder ? Bar(index, low: 85m, high: 110m, close: 105m) : Bar(index, prices[index % prices.Length]))
            .ToArray();
        BacktestInput baseline = Input(graph, candles, marketFee: 0.001m);
        var prepared = new PreparedReplay(baseline.Timeframe, candles.Take(3), candles.Skip(3));
        PreparedReplay window = prepared.Window(start, count: 12);
        var split = OutOfSampleSplit.Create(fraction: 0.25m);
        var windowInput = new BacktestInput(baseline.Graph, baseline.Selections, baseline.Settings, baseline.Rules, window, split);
        var copiedInput = new BacktestInput(
            baseline.Graph,
            baseline.Selections,
            baseline.Settings,
            baseline.Rules,
            baseline.Timeframe,
            candles.Skip(start).Take(3).ToArray(),
            candles.Skip(3 + start).Take(12).ToArray(),
            split);
        var engine = new BacktestEngine();
        var windowObserver = new MetricsObserver(windowInput.Settings.StartingCapital, windowInput.SampleRanges);
        var copiedObserver = new MetricsObserver(copiedInput.Settings.StartingCapital, copiedInput.SampleRanges);
        var options = new BacktestRunOptions { RetainLedger = false };
        BacktestResult actual = engine.Run(windowInput, windowObserver, options: options);
        BacktestResult expected = engine.Run(copiedInput, copiedObserver, options: options);

        actual.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        actual.Terminal.Outcome.ShouldBe(expected.Terminal.Outcome);
        actual.Terminal.ProcessedCandles.ShouldBe(expected.Terminal.ProcessedCandles);
        actual.Terminal.Context.ShouldBe(expected.Terminal.Context);
        actual.Terminal.Diagnostic.ShouldBe(expected.Terminal.Diagnostic);
        actual.Terminal.Equity.ShouldBe(expected.Terminal.Equity);
        actual.Terminal.UnrealizedPnl.ShouldBe(expected.Terminal.UnrealizedPnl);
        actual.Terminal.TotalPnl.ShouldBe(expected.Terminal.TotalPnl);
        AccountValues(actual.Terminal.Account).ShouldBe(AccountValues(expected.Terminal.Account));
        actual.Terminal.Account.Orders.ShouldBe(expected.Terminal.Account.Orders);
        actual.Ledger.ShouldBeEmpty();
        actual.LedgerCount.ShouldBe(expected.LedgerCount);
        actual.FillCount.ShouldBe(expected.FillCount);
        actual.OrderCount.ShouldBe(expected.OrderCount);
        actual.Diagnostics.ShouldBe(expected.Diagnostics);
        AssertMetrics(windowObserver.Snapshot.Full, copiedObserver.Snapshot.Full);
        AssertMetrics(windowObserver.Snapshot.InSample, copiedObserver.Snapshot.InSample);
        AssertMetrics(windowObserver.Snapshot.OutOfSample.ShouldNotBeNull(), copiedObserver.Snapshot.OutOfSample.ShouldNotBeNull());
        windowObserver.Snapshot.Diagnostics.ShouldBe(copiedObserver.Snapshot.Diagnostics);
    }

    private static void AssertMetrics(SampleMetrics actual, SampleMetrics expected)
    {
        actual.StartIndex.ShouldBe(expected.StartIndex);
        actual.EndExclusive.ShouldBe(expected.EndExclusive);
        actual.ObservedCandles.ShouldBe(expected.ObservedCandles);
        actual.BoundaryEquity.ShouldBe(expected.BoundaryEquity);
        actual.EndEquity.ShouldBe(expected.EndEquity);
        actual.TotalPnl.ShouldBe(expected.TotalPnl);
        actual.RealizedPnl.ShouldBe(expected.RealizedPnl);
        actual.UnrealizedPnl.ShouldBe(expected.UnrealizedPnl);
        actual.ReturnPercent.ShouldBe(expected.ReturnPercent);
        actual.FeesBase.ShouldBe(expected.FeesBase);
        actual.FeesQuote.ShouldBe(expected.FeesQuote);
        actual.MaxDrawdownPercent.ShouldBe(expected.MaxDrawdownPercent);
        actual.Executions.ShouldBe(expected.Executions);
    }

    [Test]
    public void RejectsEmptyReplay()
    {
        Should.Throw<ArgumentException>(() => new PreparedReplay(Timeframe.Parse("1h"), [Bar(0)], Array.Empty<Candle>()));
    }

    [TestCase("gap")]
    [TestCase("duplicate")]
    [TestCase("backwards")]
    [TestCase("grid")]
    [TestCase("offset")]
    [TestCase("ohlc")]
    [TestCase("volume")]
    public void ValidatesHistoryIncludingWarmupReplayBoundary(string invalid)
    {
        Candle candle = invalid switch
        {
            "gap" => Bar(2),
            "duplicate" => Bar(0),
            "backwards" => Bar(-1),
            "grid" => Bar(1) with { OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(90) },
            "offset" => Bar(1) with { OpenTime = Bar(1).OpenTime.ToOffset(TimeSpan.FromHours(1)) },
            "ohlc" => Bar(1) with { High = 99m },
            _ => Bar(1) with { Volume = -1m }
        };

        Should.Throw<ArgumentException>(() => new PreparedReplay(Timeframe.Parse("1h"), [Bar(0)], [candle]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InSampleSliceExactlyEqualsReplayBuiltFromCopiedCandles(bool ladder)
    {
        StrategyGraph graph = ladder ? LadderStrategyGraphGenerator.Generate() : SmaStrategyGraphGenerator.Generate();
        Candle[] candles = Enumerable.Range(start: 0, count: 12)
            .Select(index => ladder ? Bar(index, low: 85m, high: 110m, close: 105m) : Bar(index, index % 4 < 2 ? 100m : 110m))
            .ToArray();
        BacktestInput baseline = Input(graph, candles);
        Candle[] warmup = candles.Take(3).ToArray();
        Candle[] replay = candles.Skip(3).ToArray();
        var prepared = new PreparedReplay(baseline.Timeframe, warmup, replay);
        int inSampleBars = OutOfSampleSplit.Create(0.3m).Split(replay.Length).InSampleBars;
        PreparedReplay slice = prepared.Slice(inSampleBars);
        var slicedInput = new BacktestInput(baseline.Graph, baseline.Selections, baseline.Settings, baseline.Rules, slice);
        var copiedInput = new BacktestInput(
            baseline.Graph,
            baseline.Selections,
            baseline.Settings,
            baseline.Rules,
            baseline.Timeframe,
            warmup.ToArray(),
            replay.Take(inSampleBars).ToArray());
        var engine = new BacktestEngine();
        BacktestResult sliced = engine.Run(slicedInput);
        BacktestResult copied = engine.Run(copiedInput);

        slicedInput.Warmup.ShouldBeSameAs(slice.Warmup);
        slicedInput.Replay.ShouldBeSameAs(slice.Replay);
        sliced.Terminal.Outcome.ShouldBe(copied.Terminal.Outcome);
        sliced.Terminal.ProcessedCandles.ShouldBe(copied.Terminal.ProcessedCandles);
        sliced.Terminal.Context.ShouldBe(copied.Terminal.Context);
        sliced.Terminal.Diagnostic.ShouldBe(copied.Terminal.Diagnostic);
        sliced.Terminal.Equity.ShouldBe(copied.Terminal.Equity);
        sliced.Terminal.UnrealizedPnl.ShouldBe(copied.Terminal.UnrealizedPnl);
        sliced.Terminal.TotalPnl.ShouldBe(copied.Terminal.TotalPnl);
        AccountValues(sliced.Terminal.Account).ShouldBe(AccountValues(copied.Terminal.Account));
        sliced.Terminal.Account.Orders.ShouldBe(copied.Terminal.Account.Orders);
        sliced.Ledger.ShouldBe(copied.Ledger);
        slicedInput.SampleRanges.ShouldBe(copiedInput.SampleRanges);
    }

    [Test]
    public void RevalidatesSelectionsForEachInputSharingPreparedHistory()
    {
        BacktestInput baseline = Input(StartBuy(), [Bar(0)]);
        var prepared = new PreparedReplay(baseline.Timeframe, baseline.Warmup, baseline.Replay);
        var invalid = new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [new PortReference("buy", "amount")] = 10m });

        Should.Throw<BacktestInputException>(() => new BacktestInput(baseline.Graph, invalid, baseline.Settings, baseline.Rules, prepared));
        var valid = new BacktestInput(baseline.Graph, EngineExecutionInputs.Empty, baseline.Settings, baseline.Rules, prepared);
        valid.Replay.ShouldBeSameAs(prepared.Replay);
    }

    private static decimal?[] AccountValues(AccountSnapshot account)
    {
        return
        [
            account.StartingCapital, account.QuoteTotal, account.QuoteReserved, account.QuoteAvailable,
            account.BaseTotal, account.BaseReserved, account.BaseAvailable, account.CostBasis,
            account.RealizedPnl, account.FeesBase, account.FeesQuote, account.LastBuyPrice
        ];
    }
}