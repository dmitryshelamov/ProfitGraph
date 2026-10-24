using System.Numerics;

using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Metrics;

namespace ProfitGraph.Tests.Unit.Domain.Optimization;

public sealed class OptimizationTrialResultTests
{
    [Test]
    public void ObjectivesUseOnlyExactInSampleMetricsAndUnavailableRatioStaysNull()
    {
        var constraints = new OptimizationConstraints();
        OptimizationTrial trial = Trial(value: 1m);
        SampleMetrics metrics = Metrics();
        OptimizationTrialResult.Evaluate(trial, metrics, OptimizationObjective.InSampleReturnPercent, constraints).ObjectiveValue.ShouldBe(12m);
        OptimizationTrialResult.Evaluate(trial, metrics, OptimizationObjective.InSampleTotalPnl, constraints).ObjectiveValue.ShouldBe(123m);
        OptimizationTrialResult.Evaluate(trial, metrics, OptimizationObjective.InSampleReturnToDrawdown, constraints).ObjectiveValue.ShouldBe(3m);
        OptimizationTrialResult.Evaluate(trial, metrics with { MaxDrawdownPercent = 0m }, OptimizationObjective.InSampleReturnToDrawdown, constraints)
            .ObjectiveValue.ShouldBeNull();
        OptimizationTrialResult.Evaluate(trial, metrics with { ReturnPercent = null }, OptimizationObjective.InSampleReturnToDrawdown, constraints)
            .ObjectiveValue.ShouldBeNull();
        OptimizationTrialResult.Evaluate(
                trial,
                metrics with { ReturnPercent = decimal.MaxValue, MaxDrawdownPercent = 0.1m },
                OptimizationObjective.InSampleReturnToDrawdown,
                constraints)
            .ObjectiveValue.ShouldBeNull();
    }

    [Test]
    public void ConstraintsRejectMissingDrawdownAndBoundsButAcceptExactDefaults()
    {
        var constraints = new OptimizationConstraints();
        constraints.Accepts(Metrics() with { Executions = 1, MaxDrawdownPercent = 50m }).ShouldBeTrue();
        constraints.Accepts(Metrics() with { Executions = 0 }).ShouldBeFalse();
        constraints.Accepts(Metrics() with { MaxDrawdownPercent = 50.0001m }).ShouldBeFalse();
        constraints.Accepts(Metrics() with { MaxDrawdownPercent = null }).ShouldBeFalse();
        OptimizationTrialResult result = OptimizationTrialResult.Evaluate(
            Trial(value: 1m),
            Metrics() with { Executions = 0 },
            OptimizationObjective.InSampleReturnPercent,
            constraints);
        result.Status.ShouldBe(OptimizationTrialStatus.RejectedByConstraint);
        result.ObjectiveValue.ShouldBeNull();
        Should.Throw<OptimizationRuleException>(() => new OptimizationConstraints(minExecutions: -1)).Code.ShouldBe(OptimizationRuleException.InvalidConstraints);
        Should.Throw<OptimizationRuleException>(() => new OptimizationConstraints(maxDrawdownPercent: -1m));
    }

    [Test]
    public void RankingSortsObjectiveDescendingThenNumericTupleAndKeepsUnavailableResultsAtEnd()
    {
        var constraints = new OptimizationConstraints();
        OptimizationTrialResult low = OptimizationTrialResult.Evaluate(Trial(value: 2m), Metrics(), OptimizationObjective.InSampleReturnPercent, constraints);
        OptimizationTrialResult high = OptimizationTrialResult.Evaluate(Trial(value: 10m), Metrics(), OptimizationObjective.InSampleReturnPercent, constraints);
        OptimizationTrialResult best = OptimizationTrialResult.Evaluate(
            Trial(value: 20m),
            Metrics() with { ReturnPercent = 13m },
            OptimizationObjective.InSampleReturnPercent,
            constraints);
        OptimizationTrialResult missing = OptimizationTrialResult.Evaluate(
            Trial(value: 1m),
            Metrics() with { ReturnPercent = null },
            OptimizationObjective.InSampleReturnPercent,
            constraints);
        OptimizationTrialResult failed = OptimizationTrialResult.Failed(Trial(value: 3m), "valuation_overflow");
        OptimizationTrialResult rejected = OptimizationTrialResult.RejectedByTemplate(Trial(value: 4m));
        OptimizationRanking.Rank([rejected, high, failed, missing, best, low]).ShouldBe(new[] { best, low, high, missing, failed, rejected });
        OptimizationRanking.Rank([low, best, missing, failed, high, rejected]).ShouldBe(new[] { best, low, high, missing, failed, rejected });
        failed.FailureCode.ShouldBe("valuation_overflow");
        rejected.Status.ShouldBe(OptimizationTrialStatus.RejectedByTemplate);
    }

    private static OptimizationTrial Trial(decimal value)
    {
        return new OptimizationTrial(
            Index: 0,
            BigInteger.Zero,
            new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [new PortReference("x", "value")] = value }));
    }

    private static SampleMetrics Metrics()
    {
        return new SampleMetrics(
            StartIndex: 0,
            EndExclusive: 2,
            ObservedCandles: 2,
            BoundaryEquity: 1000m,
            EndEquity: 1123m,
            TotalPnl: 123m,
            RealizedPnl: 123m,
            UnrealizedPnl: 0m,
            ReturnPercent: 12m,
            FeesBase: 0m,
            FeesQuote: 0m,
            MaxDrawdownPercent: 4m,
            Executions: 1);
    }
}