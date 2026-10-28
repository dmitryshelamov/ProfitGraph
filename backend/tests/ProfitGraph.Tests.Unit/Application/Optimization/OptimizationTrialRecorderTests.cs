using NSubstitute;

using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Application.Optimization.RunOptimization;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Application.Backtesting;

namespace ProfitGraph.Tests.Unit.Application.Optimization;

public sealed class OptimizationTrialRecorderTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = From.AddDays(30);

    [Test]
    public async Task BatchesAreStoredWithTheOptimizationAndContinueTheSequence()
    {
        OptimizationRun optimization = Searching();
        var trials = Substitute.For<IOptimizationTrialStore>();
        var optimizations = Substitute.For<IOptimizationRunRepository>();
        var written = new List<OptimizationTrialInternal>();
        trials.When(store => store.Write(optimization.Id, Arg.Any<IReadOnlyList<OptimizationTrialInternal>>(), Arg.Any<CancellationToken>()))
            .Do(call => written.AddRange(call.ArgAt<IReadOnlyList<OptimizationTrialInternal>>(position: 1)));
        OptimizationTrialRecorder recorder = Recorder(optimization, trials, optimizations);

        await recorder.Add(windowIndex: 0, Trial(index: 0));
        await recorder.Add(windowIndex: 0, Trial(index: 1));
        await recorder.Add(windowIndex: 0, Trial(index: 2));
        await recorder.Flush();

        written.Select(trial => trial.Sequence).ShouldBe(new long[] { 1, 2, 3 });
        optimization.Counters.Executed.ShouldBe(expected: 3);
        await optimizations.Received(requiredNumberOfCalls: 2).Save(optimization, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AFailedCommitIsNotCountedAgainByALaterFlush()
    {
        OptimizationRun optimization = Searching();
        var trials = Substitute.For<IOptimizationTrialStore>();
        var optimizations = Substitute.For<IOptimizationRunRepository>();
        var failure = new InvalidOperationException("connection lost");
        trials.Write(Arg.Any<OptimizationId>(), Arg.Any<IReadOnlyList<OptimizationTrialInternal>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw failure, _ => Task.CompletedTask);
        OptimizationTrialRecorder recorder = Recorder(optimization, trials, optimizations);
        await recorder.Add(windowIndex: 0, Trial(index: 0));

        (await Should.ThrowAsync<InvalidOperationException>(() => recorder.Add(windowIndex: 0, Trial(index: 1)).AsTask())).ShouldBeSameAs(failure);

        // The handler flushes again in its finally block; the failure must stay the reason and nothing may be stored twice.
        await recorder.Flush();

        optimization.Counters.Executed.ShouldBe(expected: 2);
        await trials.Received(requiredNumberOfCalls: 1).Write(optimization.Id, Arg.Any<IReadOnlyList<OptimizationTrialInternal>>(), Arg.Any<CancellationToken>());
        await optimizations.DidNotReceive().Save(Arg.Any<OptimizationRun>(), Arg.Any<CancellationToken>());
        await Should.ThrowAsync<InvalidOperationException>(() => recorder.Add(windowIndex: 0, Trial(index: 2)).AsTask());
    }

    private static OptimizationTrialRecorder Recorder(OptimizationRun optimization, IOptimizationTrialStore trials, IOptimizationRunRepository optimizations)
    {
        return new OptimizationTrialRecorder(
            optimization,
            [],
            ranksBest: true,
            batchSize: 2,
            trials,
            optimizations,
            new ImmediateTransactions(),
            Substitute.For<IBackgroundJobProgress>());
    }

    private static OptimizationRun Searching()
    {
        var optimization = OptimizationRun.Create(
            new OptimizationId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            new StrategyId(Guid.NewGuid()),
            StrategyVersionNumber.First,
            new OptimizationConfiguration(
                Backtest(),
                new Dictionary<string, DecimalOptimizeParamValue> { ["fastPeriod"] = new DecimalOptimizeParamValue(Min: 2m, Max: 5m, Step: 1m) },
                OptimizationSearchMethod.Grid,
                OptimizationObjective.InSampleReturnPercent,
                new OptimizationConstraints(),
                budget: 10,
                seed: 42,
                finalistCount: 3),
            Now);
        optimization.BeginLoadingHistory();
        optimization.AttachHistory(
            new BacktestHistoryInfo(
                "history",
                From.AddHours(-10),
                From,
                From.AddDays(10),
                WarmupCount: 10,
                ReplayCount: 240,
                new InstrumentRulesSnapshotId(Guid.NewGuid()),
                "BTC",
                "USDT",
                new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.01m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8)));

        return optimization;
    }

    private static BacktestConfiguration Backtest()
    {
        return new BacktestConfiguration(
            new CandleSeries(new VenueId("bybit"), new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h")),
            From,
            From.AddDays(10),
            Now,
            new Dictionary<string, decimal> { ["buyAmount"] = 50m },
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            OutOfSampleSplit.Default,
            warmupCandles: 10,
            BacktestResult.ExecutionPolicyVersion,
            new string(c: 'a', count: 64));
    }

    private static OptimizationTrialResult Trial(int index)
    {
        var trial = new OptimizationTrial(
            index,
            index,
            new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [new PortReference("fastPeriod", "value")] = index + 2m }));

        return OptimizationTrialResult.Restore(trial, metrics: null, OptimizationTrialStatus.Completed, 1m + index, failureCode: null, elapsedMs: 0);
    }
}