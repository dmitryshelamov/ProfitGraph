using NSubstitute;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Backtesting.Execution;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Application.Backtesting;

/// <summary>
///     The bounded channel between a replay and storage (ADR 0012): order, batches, cancellation and storage failures.
/// </summary>
public sealed class BacktestReplaySessionTests
{
    private static readonly BacktestExecutionOptions SmallBatches = new BacktestExecutionOptions { BatchSize = 7, ChannelCapacity = 8 };

    [Test]
    public async Task EveryFactIsStoredInReplayOrderInBoundedBatches()
    {
        var store = new RecordingFactStore();
        var transactions = new ImmediateTransactions();
        var progress = Substitute.For<IBackgroundJobProgress>();
        BacktestInput input = Input();
        BacktestReplaySession session = Session(store, transactions, progress, input);

        BacktestResult result = await Task.Run(() => new BacktestEngine().Run(input, session), CancellationToken.None);
        BacktestRunMetrics? metrics = await session.Complete();

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Ledger.Count.ShouldBeGreaterThan(0);
        store.Batches.SelectMany(batch => batch.Ledger).ShouldBe(result.Ledger);
        BacktestCandleObservation[] candles = [.. store.Batches.SelectMany(batch => batch.Candles)];
        candles.Select(candle => candle.Snapshot.Sequence).ShouldBe(Enumerable.Range(start: 1, input.Replay.Count).Select(index => (long)index));
        candles[^1].LedgerThrough.ShouldBe(result.Ledger[^1].Sequence);
        store.Batches.ShouldAllBe(batch => batch.Ledger.Count + batch.Candles.Count + batch.Diagnostics.Count <= SmallBatches.BatchSize);
        store.Batches.SelectMany(batch => batch.Diagnostics)
            .Select(record => record.Sequence)
            .ShouldBe(Enumerable.Range(start: 1, store.Batches.Sum(batch => batch.Diagnostics.Count)).Select(index => (long)index));
        transactions.Count.ShouldBe(store.Batches.Count);
        metrics.ShouldNotBeNull();
        metrics.Full.ObservedCandles.ShouldBe(input.Replay.Count);
        metrics.OutOfSample.ShouldNotBeNull();
        (metrics.InSample.ObservedCandles + metrics.OutOfSample.ObservedCandles).ShouldBe(input.Replay.Count);
        await progress.Received().Report(input.Replay.Count, input.Replay.Count, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CancelledReplayStoresEveryFactItReported()
    {
        // Disposed after the replay, which is the only user of the token source.
        var cancellation = new CancellationTokenSource();
        var store = new RecordingFactStore
        {
            BeforeAppend = async number =>
            {
                if (number == 3)
                {
                    await cancellation.CancelAsync();
                }

                await Task.Delay(millisecondsDelay: 1, CancellationToken.None);
            }
        };
        BacktestInput input = Input();
        BacktestReplaySession session = Session(store, new ImmediateTransactions(), Substitute.For<IBackgroundJobProgress>(), input);
        BacktestResult result;

        try
        {
            result = await Task.Run(() => new BacktestEngine().Run(input, session, cancellation.Token), CancellationToken.None);
            await session.Complete();
        }
        finally
        {
            cancellation.Dispose();
        }

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Cancelled);
        result.Terminal.ProcessedCandles.ShouldBeLessThan(input.Replay.Count);
        store.Batches.SelectMany(batch => batch.Candles).Count().ShouldBe(result.Terminal.ProcessedCandles);
        store.Batches.SelectMany(batch => batch.Ledger).ShouldBe(result.Ledger);
        store.Batches.SelectMany(batch => batch.Diagnostics).ShouldContain(record => record.Diagnostic.Code == "Cancelled");
    }

    [Test]
    public async Task StorageFailureStopsTheReplayAndFailsCompletion()
    {
        var store = new RecordingFactStore
        {
            BeforeAppend = number => number == 2 ? throw new InvalidOperationException("The database is gone.") : Task.CompletedTask
        };
        BacktestInput input = Input();
        BacktestReplaySession session = Session(store, new ImmediateTransactions(), Substitute.For<IBackgroundJobProgress>(), input);

        BacktestResult result = await Task.Run(() => new BacktestEngine().Run(input, session), CancellationToken.None);
        var error = await Should.ThrowAsync<BacktestException>(session.Complete);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Failed);
        result.Terminal.ProcessedCandles.ShouldBeLessThan(input.Replay.Count);
        error.Code.ShouldBe(BacktestErrorCodes.PersistenceFailed);
        error.Message.ShouldContain("The database is gone.");
    }

    [Test]
    public async Task CompletionIsSingleUse()
    {
        BacktestInput input = Input();
        BacktestReplaySession session = Session(new RecordingFactStore(), new ImmediateTransactions(), Substitute.For<IBackgroundJobProgress>(), input);
        await Task.Run(() => new BacktestEngine().Run(input, session), CancellationToken.None);
        await session.Complete();

        await Should.ThrowAsync<InvalidOperationException>(session.Complete);
    }

    private static BacktestReplaySession Session(
        RecordingFactStore store,
        ImmediateTransactions transactions,
        IBackgroundJobProgress progress,
        BacktestInput input)
    {
        return new BacktestReplaySession(
            new BacktestRunId(Guid.NewGuid()),
            store,
            transactions,
            progress,
            SmallBatches,
            input.Settings.StartingCapital,
            input.SampleRanges);
    }

    /// <summary>
    ///     A fast SMA crossover on a price wave, so the replay trades on many candles.
    /// </summary>
    private static BacktestInput Input()
    {
        CompiledTradingGraph plan = new TradingGraphCompiler().Compile(SmaStrategyGraphGenerator.Generate(), ProductionTradingNodeTests.Catalog).Graph.ShouldNotBeNull();
        Candle[] candles =
        [
            .. Enumerable.Range(start: 0, count: 200)
                .Select(index =>
                {
                    decimal open = Price(index - 1);
                    decimal close = Price(index);

                    return new Candle(DateTimeOffset.UnixEpoch.AddHours(index), open, Math.Max(open, close) + 1m, Math.Min(open, close) - 1m, close, Volume: 1m);
                })
        ];

        return new BacktestInput(
            plan,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.01m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            [],
            candles,
            OutOfSampleSplit.Default);
    }

    private static decimal Price(int index)
    {
        int phase = (index % 10 + 10) % 10;

        return 100m + (phase < 5 ? phase : 10 - phase) * 2m;
    }
}