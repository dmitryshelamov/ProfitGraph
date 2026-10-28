using System.Numerics;

using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Optimization.Search;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Metrics;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Domain.Optimization.Search;

public sealed class OptimizationNeighborhoodScoresTests
{
    [Test]
    public void EdgeTrialCountsOnlyEvaluatedNeighborsAndRejectedObjectivesAreExcluded()
    {
        OptimizationSearchSpace space = Space();
        OptimizationTrialResult[] results =
        [
            Result(space.TrialAt(index: 0, gridIndex: 0), objective: 10m),
            Result(space.TrialAt(index: 1, gridIndex: 1), objective: 20m, accepted: false),
            Result(space.TrialAt(index: 2, gridIndex: 2), objective: 30m)
        ];
        IReadOnlyDictionary<int, OptimizationNeighborhoodScore> scores = OptimizationNeighborhoodScores.Calculate(space, results);
        scores[0].ShouldBe(new OptimizationNeighborhoodScore(Evaluated: 1, Eligible: 0, Mean: null));
        scores[1].ShouldBe(new OptimizationNeighborhoodScore(Evaluated: 2, Eligible: 2, Mean: 20m));
        scores[1].Evaluated.ShouldBe(2);
        scores[1].Eligible.ShouldBe(2);
        scores[1].Mean.ShouldBe(20m);
        scores[2].ShouldBe(new OptimizationNeighborhoodScore(Evaluated: 1, Eligible: 0, Mean: null));
    }

    [Test]
    public void FailedNeighborsCountAsEvaluatedAndTemplateRejectionsHaveNoScore()
    {
        OptimizationSearchSpace space = Space();
        OptimizationTrialResult[] results =
        [
            OptimizationTrialResult.Failed(space.TrialAt(index: 0, gridIndex: 0), "failure"),
            Result(space.TrialAt(index: 1, gridIndex: 1), objective: 0m),
            OptimizationTrialResult.RejectedByTemplate(space.TrialAt(index: 2, gridIndex: 2))
        ];
        IReadOnlyDictionary<int, OptimizationNeighborhoodScore> scores = OptimizationNeighborhoodScores.Calculate(space, results);
        scores.Count.ShouldBe(2);
        scores[1].ShouldBe(new OptimizationNeighborhoodScore(Evaluated: 1, Eligible: 0, Mean: null));
        scores.ContainsKey(2).ShouldBeFalse();
    }

    [Test]
    public void OverflowMakesMeanUnavailableWithoutLosingNeighborCounts()
    {
        OptimizationSearchSpace space = Space();
        OptimizationTrialResult[] results =
        [
            Result(space.TrialAt(index: 0, gridIndex: 0), decimal.MaxValue),
            Result(space.TrialAt(index: 1, gridIndex: 1), objective: 0m),
            Result(space.TrialAt(index: 2, gridIndex: 2), decimal.MaxValue)
        ];
        IReadOnlyDictionary<int, OptimizationNeighborhoodScore> scores = OptimizationNeighborhoodScores.Calculate(space, results, [new BigInteger(2)]);
        scores[1].ShouldBe(new OptimizationNeighborhoodScore(Evaluated: 2, Eligible: 2, Mean: null));
        scores[0].ShouldBe(new OptimizationNeighborhoodScore(Evaluated: 2, Eligible: 2, decimal.MaxValue / 2));
    }

    private static OptimizationSearchSpace Space()
    {
        return new OptimizationSearchSpace([new OptimizationParameterRange(new PortReference("x", "value"), min: 0m, max: 2m, step: 1m, ValueType.Integer)]);
    }

    private static OptimizationTrialResult Result(OptimizationTrial trial, decimal objective, bool accepted = true)
    {
        var metrics = new SampleMetrics(
            StartIndex: 0,
            EndExclusive: 1,
            ObservedCandles: 1,
            BoundaryEquity: 1000m,
            EndEquity: 1000m,
            TotalPnl: 0m,
            RealizedPnl: 0m,
            UnrealizedPnl: 0m,
            objective,
            FeesBase: 0m,
            FeesQuote: 0m,
            MaxDrawdownPercent: 0m,
            accepted ? 1 : 0);

        return OptimizationTrialResult.Evaluate(trial, metrics, OptimizationObjective.InSampleReturnPercent, new OptimizationConstraints());
    }
}