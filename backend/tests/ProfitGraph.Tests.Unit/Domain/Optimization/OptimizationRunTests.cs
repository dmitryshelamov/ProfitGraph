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

namespace ProfitGraph.Tests.Unit.Domain.Optimization;

public sealed class OptimizationRunTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = From.AddDays(30);

    [Test]
    public void SearchMovesThroughItsStagesAndCountsTrials()
    {
        OptimizationRun optimization = Create();
        optimization.BeginLoadingHistory();
        optimization.AttachHistory(History());
        optimization.Stage.ShouldBe(OptimizationStage.Searching);
        optimization.PlanTrials(plannedTrials: 3);
        optimization.RecordTrials([Trial(index: 0, OptimizationTrialStatus.Completed), Trial(index: 1, OptimizationTrialStatus.RejectedByTemplate)], Best(index: 0));
        optimization.RecordTrials([Trial(index: 2, OptimizationTrialStatus.Failed), Trial(index: 3, OptimizationTrialStatus.RejectedByConstraint)], Best(index: 0));
        optimization.CompleteSearch();

        optimization.Stage.ShouldBe(OptimizationStage.StartingFinalists);
        optimization.Counters.ShouldBe(new OptimizationCounters(Completed: 1, RejectedByConstraint: 1, RejectedByTemplate: 1, Failed: 1));
        optimization.Counters.Executed.ShouldBe(3);
        optimization.Best.ShouldBe(Best(index: 0));
    }

    [Test]
    public void TrialsAreRecordedOnlyWhileSearchingAndNeverBeyondThePlan()
    {
        OptimizationRun optimization = Create();
        Rule(() => optimization.RecordTrials([], best: null));
        Rule(() => optimization.AttachHistory(History()));
        optimization.BeginLoadingHistory();
        Rule(() => optimization.BeginLoadingHistory());
        optimization.AttachHistory(History());
        optimization.PlanTrials(plannedTrials: 1);
        Rule(() => optimization.PlanTrials(plannedTrials: 11));
        Rule(() => optimization.RecordTrials([Trial(index: 0, OptimizationTrialStatus.Failed), Trial(index: 1, OptimizationTrialStatus.Failed)], best: null));
        Rule(() => optimization.RecordTrials([Trial(index: 0, OptimizationTrialStatus.Failed)], Best(index: 0)));
        optimization.RecordTrials([Trial(index: 0, OptimizationTrialStatus.Completed)], Best(index: 0));
        Rule(() => optimization.RecordTrials([], best: null));
    }

    [Test]
    public void HistoryMustCoverTheFrozenPeriod()
    {
        OptimizationRun optimization = Create();
        optimization.BeginLoadingHistory();

        Should.Throw<BacktestRunRuleException>(() => optimization.AttachHistory(History() with { From = From.AddHours(1) }))
            .Code.ShouldBe(BacktestRunRuleException.InvalidHistory);
    }

    [Test]
    public void RestoreChecksTheSameRulesAsTheTransitions()
    {
        OptimizationRun restored = OptimizationRun.Restore(
            new OptimizationId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            new StrategyId(Guid.NewGuid()),
            StrategyVersionNumber.First,
            Now,
            Configuration(),
            OptimizationStage.StartingFinalists,
            History(),
            new OptimizationCounters(Completed: 2, RejectedByConstraint: 0, RejectedByTemplate: 5, Failed: 1),
            Best(index: 4),
            plannedTrials: 3);

        restored.Stage.ShouldBe(OptimizationStage.StartingFinalists);
        restored.Best.ShouldBe(Best(index: 4));
        Rule(() => OptimizationRun.Restore(
            new OptimizationId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            new StrategyId(Guid.NewGuid()),
            StrategyVersionNumber.First,
            Now,
            Configuration(),
            OptimizationStage.LoadingHistory,
            history: null,
            new OptimizationCounters(Completed: 1, RejectedByConstraint: 0, RejectedByTemplate: 0, Failed: 0),
            best: null,
            plannedTrials: null));
    }

    [Test]
    public void ConfigurationNeedsRangesOnParametersThatAreNotFixed()
    {
        BacktestConfiguration backtest = Backtest();
        var constraints = new OptimizationConstraints();

        foreach (Dictionary<string, DecimalOptimizeParamValue> ranges in new[]
                 {
                     new Dictionary<string, DecimalOptimizeParamValue>(),
                     new Dictionary<string, DecimalOptimizeParamValue> { ["buyAmount"] = new DecimalOptimizeParamValue(Min: 1m, Max: 2m, Step: 1m) }
                 })
        {
            Should.Throw<OptimizationRuleException>(() => new OptimizationConfiguration(
                    backtest,
                    ranges,
                    OptimizationSearchMethod.Grid,
                    OptimizationObjective.InSampleReturnPercent,
                    constraints,
                    budget: 10,
                    seed: 0,
                    finalistCount: 1))
                .Code.ShouldBe(OptimizationRuleException.InvalidConfiguration);
        }

        Should.Throw<OptimizationRuleException>(() => Configuration(budget: 0)).Code.ShouldBe(OptimizationRuleException.InvalidConfiguration);
        Should.Throw<OptimizationRuleException>(() => Configuration(finalistCount: 11)).Code.ShouldBe(OptimizationRuleException.InvalidConfiguration);
    }

    private static void Rule(Action action)
    {
        Should.Throw<OptimizationRuleException>(action).Code.ShouldBe(OptimizationRuleException.InvalidTransition);
    }

    private static OptimizationRun Create()
    {
        return OptimizationRun.Create(
            new OptimizationId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            new StrategyId(Guid.NewGuid()),
            StrategyVersionNumber.First,
            Configuration(),
            Now);
    }

    private static OptimizationConfiguration Configuration(int budget = 10, int finalistCount = 3)
    {
        return new OptimizationConfiguration(
            Backtest(),
            new Dictionary<string, DecimalOptimizeParamValue> { ["fastPeriod"] = new DecimalOptimizeParamValue(Min: 2m, Max: 5m, Step: 1m) },
            OptimizationSearchMethod.Grid,
            OptimizationObjective.InSampleReturnPercent,
            new OptimizationConstraints(),
            budget,
            seed: 42,
            finalistCount);
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

    private static BacktestHistoryInfo History()
    {
        return new BacktestHistoryInfo(
            "history",
            From.AddHours(-10),
            From,
            From.AddDays(10),
            WarmupCount: 10,
            ReplayCount: 240,
            new InstrumentRulesSnapshotId(Guid.NewGuid()),
            "BTC",
            "USDT",
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.01m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8));
    }

    private static OptimizationTrialResult Trial(int index, OptimizationTrialStatus status)
    {
        var trial = new OptimizationTrial(
            index,
            index,
            new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [new PortReference("fastPeriod", "value")] = index + 2m }));

        return status switch
        {
            OptimizationTrialStatus.RejectedByTemplate => OptimizationTrialResult.RejectedByTemplate(trial),
            OptimizationTrialStatus.Failed => OptimizationTrialResult.Failed(trial, "trial_exception"),
            _ => OptimizationTrialResult.Restore(
                trial,
                metrics: null,
                status,
                status == OptimizationTrialStatus.Completed ? 1m : null,
                failureCode: null,
                elapsedMs: 0)
        };
    }

    private static OptimizationBestTrial Best(int index)
    {
        return new OptimizationBestTrial(index, Objective: 1m);
    }
}