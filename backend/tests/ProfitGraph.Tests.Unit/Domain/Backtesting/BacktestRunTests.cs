using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Tests.Unit.Domain.Backtesting;

public sealed class BacktestRunTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = Start.AddDays(1).AddMinutes(30);
    private static readonly CandleSeries Series = new CandleSeries(new VenueId("bybit"), new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h"));
    private static readonly string Fingerprint = new string(c: 'A', count: 64);

    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.000001m,
        priceTick: 0.01m,
        minQuantity: 0m,
        minNotional: 5m,
        basePrecision: 6,
        quotePrecision: 8);

    [Test]
    public void NewRunIsNotStartedAndBelongsToItsCreator()
    {
        var owner = UserId.New();
        BacktestRun run = Run(owner: owner);

        run.ShouldSatisfyAllConditions(
            () => run.Stage.ShouldBeNull(),
            () => run.History.ShouldBeNull(),
            () => run.Result.ShouldBeNull(),
            () => run.IsOwnedBy(owner).ShouldBeTrue(),
            () => run.IsOwnedBy(UserId.New()).ShouldBeFalse());
    }

    [Test]
    public void RunLoadsHistoryReplaysAndFinishes()
    {
        BacktestRun run = Run();

        run.BeginLoadingHistory();
        run.Stage.ShouldBe(BacktestRunStage.LoadingHistory);
        run.AttachHistory(History());
        run.Stage.ShouldBe(BacktestRunStage.Replaying);
        run.Finish(Result(BacktestOutcome.Completed, processed: 4));

        run.Result!.Outcome.ShouldBe(BacktestOutcome.Completed);
    }

    [Test]
    public void ExecutionStartsOnce()
    {
        BacktestRun run = Run();
        run.BeginLoadingHistory();

        Should.Throw<BacktestRunRuleException>(run.BeginLoadingHistory).Code.ShouldBe(BacktestRunRuleException.InvalidTransition);
    }

    [Test]
    public void HistoryIsAttachedOnceWhileLoading()
    {
        BacktestRun notStarted = Run();
        BacktestRun replaying = Run();
        replaying.BeginLoadingHistory();
        replaying.AttachHistory(History());

        Should.Throw<BacktestRunRuleException>(() => notStarted.AttachHistory(History())).Code.ShouldBe(BacktestRunRuleException.InvalidTransition);
        Should.Throw<BacktestRunRuleException>(() => replaying.AttachHistory(History())).Code.ShouldBe(BacktestRunRuleException.InvalidTransition);
    }

    [TestCase("other start")]
    [TestCase("other end")]
    [TestCase("too much warm-up")]
    [TestCase("wrong replay count")]
    [TestCase("unaligned")]
    [TestCase("same assets")]
    public void HistoryMustMatchTheFrozenPeriod(string mode)
    {
        BacktestRun run = Run(warmup: 2);
        run.BeginLoadingHistory();
        BacktestHistoryInfo history = History();
        BacktestHistoryInfo invalid = mode switch
        {
            "other start" => history with { HistoryFrom = Start, From = Start.AddHours(1), WarmupCount = 1, ReplayCount = 5 },
            "other end" => history with { To = Start.AddHours(5), ReplayCount = 3 },
            "too much warm-up" => history with { HistoryFrom = Start.AddHours(-1), WarmupCount = 3 },
            "wrong replay count" => history with { ReplayCount = 5 },
            "unaligned" => history with { HistoryFrom = Start.AddHours(2).AddMinutes(1) },
            _ => history with { QuoteAsset = history.BaseAsset }
        };

        Should.Throw<BacktestRunRuleException>(() => run.AttachHistory(invalid)).Code.ShouldBe(BacktestRunRuleException.InvalidHistory);
    }

    [Test]
    public void HistoryOfAMarketThatClosesCountsItsClosedIntervalsBesideItsCandles()
    {
        BacktestRun run = Run(warmup: 2);
        run.BeginLoadingHistory();
        BacktestHistoryInfo closing = History() with { WarmupCount = 1, ReplayCount = 3, WarmupClosedIntervals = 1, ReplayClosedIntervals = 1 };

        Should.Throw<BacktestRunRuleException>(() => run.AttachHistory(closing with { ReplayClosedIntervals = 0 }))
            .Code.ShouldBe(BacktestRunRuleException.InvalidHistory);
        Should.Throw<BacktestRunRuleException>(() => run.AttachHistory(closing with { WarmupClosedIntervals = -1, WarmupCount = 3 }))
            .Code.ShouldBe(BacktestRunRuleException.InvalidHistory);
        run.AttachHistory(closing);

        run.History.ShouldBe(closing);
    }

    [Test]
    public void OmittedBoundsResolveToTheVenueStartAndTheLatestClosedCandle()
    {
        BacktestRun run = Run(openPeriod: true);
        run.BeginLoadingHistory();

        run.AttachHistory(History() with { From = Start, HistoryFrom = Start, To = Start.AddDays(1), WarmupCount = 0, ReplayCount = 24 });

        run.History!.To.ShouldBe(Start.AddDays(1));
        run.Configuration.LatestClosed.ShouldBe(Start.AddDays(1));
    }

    [Test]
    public void ResultIsStoredOnceAfterHistory()
    {
        BacktestRun loading = Run();
        loading.BeginLoadingHistory();
        BacktestRun finished = Replaying();
        finished.Finish(Result(BacktestOutcome.Cancelled, processed: 2));

        Should.Throw<BacktestRunRuleException>(() => loading.Finish(Result(BacktestOutcome.Completed, processed: 4)))
            .Code.ShouldBe(BacktestRunRuleException.InvalidTransition);
        Should.Throw<BacktestRunRuleException>(() => finished.Finish(Result(BacktestOutcome.Completed, processed: 4)))
            .Code.ShouldBe(BacktestRunRuleException.InvalidTransition);
    }

    [TestCase(BacktestOutcome.Completed, arg2: 3, arg3: null)]
    [TestCase(BacktestOutcome.Cancelled, arg2: 5, arg3: null)]
    [TestCase(BacktestOutcome.Failed, arg2: 2, arg3: null)]
    [TestCase(BacktestOutcome.Completed, arg2: 4, "replay_failed")]
    public void ResultMustMatchTheReplay(BacktestOutcome outcome, int processed, string? failure)
    {
        BacktestRun run = Replaying();

        Should.Throw<BacktestRunRuleException>(() => run.Finish(Result(outcome, processed) with { FailureCode = failure }))
            .Code.ShouldBe(BacktestRunRuleException.InvalidResult);
    }

    [Test]
    public void MetricsMustCoverTheProcessedCandles()
    {
        BacktestRun run = Replaying();

        Should.Throw<BacktestRunRuleException>(() => run.Finish(Result(BacktestOutcome.Completed, processed: 4) with { Metrics = Metrics(observed: 3) }))
            .Code.ShouldBe(BacktestRunRuleException.InvalidResult);
    }

    [TestCase("start after latest")]
    [TestCase("end before start")]
    [TestCase("unaligned")]
    [TestCase("local time")]
    [TestCase("negative warm-up")]
    [TestCase("no fingerprint")]
    public void InvalidConfiguration_IsRejected(string mode)
    {
        Should.Throw<BacktestRunRuleException>(() => mode switch
            {
                "start after latest" => Configuration(from: Start.AddDays(1)),
                "end before start" => Configuration(to: Start.AddHours(2)),
                "unaligned" => Configuration(from: Start.AddMinutes(1)),
                "local time" => Configuration(from: new DateTimeOffset(Start.DateTime, TimeSpan.FromHours(3))),
                "negative warm-up" => Configuration(warmup: -1),
                _ => Configuration(fingerprint: "abc")
            })
            .Code.ShouldBe(BacktestRunRuleException.InvalidConfiguration);
    }

    [Test]
    public void RestoredRunKeepsItsStageAndRejectsAnImpossibleOne()
    {
        BacktestRun restored = BacktestRun.Restore(
            new BacktestRunId(Guid.NewGuid()),
            UserId.New(),
            StrategyId.New(),
            StrategyVersionNumber.First,
            Start,
            Configuration(),
            BacktestRunStage.Replaying,
            History(),
            Result(BacktestOutcome.Completed, processed: 4));

        restored.Result.ShouldNotBeNull();
        Should.Throw<BacktestRunRuleException>(() => BacktestRun.Restore(
            new BacktestRunId(Guid.NewGuid()),
            UserId.New(),
            StrategyId.New(),
            StrategyVersionNumber.First,
            Start,
            Configuration(),
            BacktestRunStage.Replaying,
            history: null,
            result: null));
    }

    private static BacktestRun Replaying()
    {
        BacktestRun run = Run();
        run.BeginLoadingHistory();
        run.AttachHistory(History());

        return run;
    }

    private static BacktestRun Run(UserId? owner = null, bool openPeriod = false, int warmup = 2)
    {
        return BacktestRun.Create(
            new BacktestRunId(Guid.NewGuid()),
            owner ?? UserId.New(),
            StrategyId.New(),
            StrategyVersionNumber.First,
            openPeriod ? Configuration(openPeriod: true, warmup: warmup) : Configuration(warmup: warmup),
            ObservedAt);
    }

    private static BacktestConfiguration Configuration(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int warmup = 2,
        string? fingerprint = null,
        bool openPeriod = false)
    {
        return new BacktestConfiguration(
            Series,
            openPeriod ? null : from ?? Start.AddHours(2),
            openPeriod ? null : to ?? Start.AddHours(6),
            ObservedAt,
            new Dictionary<string, decimal> { ["amount"] = 100m },
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m),
            OutOfSampleSplit.Default,
            warmup,
            BacktestResult.ExecutionPolicyVersion,
            fingerprint ?? Fingerprint);
    }

    private static BacktestHistoryInfo History()
    {
        return new BacktestHistoryInfo(
            new string(c: 'b', count: 64),
            Start,
            Start.AddHours(2),
            Start.AddHours(6),
            WarmupCount: 2,
            ReplayCount: 4,
            new InstrumentRulesSnapshotId(Guid.NewGuid()),
            "BTC",
            "USDT",
            Rules);
    }

    private static BacktestRunResult Result(BacktestOutcome outcome, int processed)
    {
        return new BacktestRunResult(
            outcome,
            processed,
            Equity: 1000m,
            TotalPnl: 0m,
            outcome == BacktestOutcome.Failed ? "replay_failed" : null,
            outcome == BacktestOutcome.Failed ? "The replay failed." : null,
            Metrics(processed));
    }

    private static BacktestRunMetrics Metrics(int observed)
    {
        var sample = new SampleMetrics(
            StartIndex: 0,
            observed,
            observed,
            BoundaryEquity: 1000m,
            EndEquity: 1000m,
            TotalPnl: 0m,
            RealizedPnl: 0m,
            UnrealizedPnl: 0m,
            ReturnPercent: 0m,
            FeesBase: 0m,
            FeesQuote: 0m,
            MaxDrawdownPercent: 0m,
            Executions: 0);

        return new BacktestRunMetrics(sample, sample, OutOfSample: null);
    }
}