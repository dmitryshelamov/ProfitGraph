using System.Numerics;

using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Optimization.Search;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Metrics;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Domain.Optimization.Search;

public sealed class OptimizationCoarseToFinePlanTests
{
    [TestCase(new[] { 57, 57 }, 2000, new[] { 3, 2 }, new[] { 20, 29 }, 580)]
    [TestCase(new[] { 15, 15 }, 100, new[] { 4, 3 }, new[] { 5, 6 }, 30)]
    [TestCase(new[] { 100, 100, 100 }, 2000, new[] { 15, 13, 13 }, new[] { 8, 9, 9 }, 648)]
    [TestCase(new[] { 8, 1000 }, 2000, new[] { 1, 13 }, new[] { 8, 78 }, 624)]
    public void GreedyMultipliersMatchContractVectors(int[] counts, int budget, int[] multipliers, int[] coarseCounts, int size)
    {
        OptimizationSearchSpace space = Space(counts);
        OptimizationCoarseToFinePlan.TryCreate(space, budget, out OptimizationCoarseToFinePlan? plan).ShouldBeTrue();
        plan.ShouldNotBeNull();
        plan.Multipliers.ShouldBe(multipliers.Select(value => new BigInteger(value)));
        plan.CoarseCounts.ShouldBe(coarseCounts.Select(value => new BigInteger(value)));
        plan.CoarseGridSize.ShouldBe(new BigInteger(size));
        OptimizationTrial[] stageOne = plan.StageOneCandidates().ToArray();
        stageOne.Length.ShouldBe(size);
        stageOne.Select(trial => trial.GridIndex).ShouldBe(stageOne.Select(trial => trial.GridIndex).Order());
        stageOne.Select(trial => trial.GridIndex).Distinct().Count().ShouldBe(size);
        stageOne.ShouldAllBe(trial => trial.Stage == 1);
    }

    [Test]
    public void EndpointAndSingletonIndicesAreIncluded()
    {
        OptimizationSearchSpace space = Space([1, 15, 15]);
        OptimizationCoarseToFinePlan.TryCreate(space, budget: 100, out OptimizationCoarseToFinePlan? plan).ShouldBeTrue();
        plan.ShouldNotBeNull();
        plan.CoarseCounts.ShouldBe(new BigInteger[] { 1, 5, 6 });
        plan.StageOneCandidates()
            .Select(trial => space.CoordinatesAt(trial.GridIndex)[1])
            .Distinct()
            .ShouldBe(new BigInteger[] { 0, 4, 8, 12, 14 });
        plan.StageOneCandidates()
            .Select(trial => space.CoordinatesAt(trial.GridIndex)[2])
            .Distinct()
            .ShouldBe(new BigInteger[] { 0, 3, 6, 9, 12, 14 });
    }

    [Test]
    public void MinimumCoarseSizeAndRefineMemoryBoundCanRejectPlans()
    {
        OptimizationCoarseToFinePlan.TryCreate(Space(Enumerable.Repeat(element: 2, count: 12).ToArray()), budget: 10000, out _).ShouldBeFalse();
        OptimizationCoarseToFinePlan.TryCreate(Space(Enumerable.Repeat(element: 1000, count: 10).ToArray()), budget: 10000, out _).ShouldBeFalse();
        OptimizationCoarseToFinePlan.TryCreate(Space([2]), budget: 5, out _).ShouldBeFalse();
    }

    [Test]
    public void RefineDeduplicatesClipsEdgesAndOrdersExactRationalDistances()
    {
        OptimizationSearchSpace space = Space([15, 15]);
        OptimizationCoarseToFinePlan.TryCreate(space, budget: 100, out OptimizationCoarseToFinePlan? plan).ShouldBeTrue();
        plan.ShouldNotBeNull();
        OptimizationTrial[] coarse = plan.StageOneCandidates().ToArray();
        BigInteger[] centers = [8 * 15 + 6, 14 * 15, 4 * 15 + 3];
        OptimizationTrialResult[] ranked = centers.Select((gridIndex, rank) =>
                Result(coarse.Single(trial => trial.GridIndex == gridIndex), 3m - rank))
            .ToArray();
        IReadOnlyList<OptimizationTrial> refined = plan.StageTwoCandidates(ranked, coarse.Select(trial => trial.GridIndex).ToHashSet(), coarse.Length);
        refined.Count.ShouldBe(106);
        refined.Select(trial => trial.GridIndex).Distinct().Count().ShouldBe(106);
        refined.ShouldAllBe(trial => trial.Stage == 2 && trial.Index >= coarse.Length);
        refined.Select(trial => trial.Index).ShouldBe(Enumerable.Range(coarse.Length, refined.Count));
        refined.Take(10)
            .Select(trial => space.CoordinatesAt(trial.GridIndex).Select(index => (int)index).ToArray())
            .ShouldBe(
                new[]
                {
                    new[] { 7, 6 }, new[] { 9, 6 }, new[] { 13, 0 }, new[] { 3, 3 }, new[] { 5, 3 },
                    new[] { 7, 5 }, new[] { 7, 7 }, new[] { 8, 5 }, new[] { 8, 7 }, new[] { 9, 5 }
                },
                ignoreOrder: false);
        refined.ShouldAllBe(trial => space.CoordinatesAt(trial.GridIndex).All(index => index >= 0 && index < 15));
    }

    [Test]
    public void EqualDistanceFromTwoCentersBelongsToTheEarlierRankedCenter()
    {
        OptimizationSearchSpace space = Space([15, 15]);
        OptimizationCoarseToFinePlan.TryCreate(space, budget: 100, out OptimizationCoarseToFinePlan? plan).ShouldBeTrue();
        plan.ShouldNotBeNull();
        OptimizationTrial[] coarse = plan.StageOneCandidates().ToArray();
        OptimizationTrialResult[] centers =
        [
            Result(coarse.Single(trial => trial.GridIndex == 8 * 15 + 6), objective: 2m),
            Result(coarse.Single(trial => trial.GridIndex == 4 * 15 + 6), objective: 1m)
        ];
        IReadOnlyList<OptimizationTrial> refined = plan.StageTwoCandidates(centers, coarse.Select(trial => trial.GridIndex).ToHashSet(), coarse.Length);
        int shared = refined.ToList().FindIndex(trial => trial.GridIndex == 6 * 15 + 6);
        int secondOnly = refined.ToList().FindIndex(trial => trial.GridIndex == 2 * 15 + 6);
        shared.ShouldBeGreaterThanOrEqualTo(0);
        secondOnly.ShouldBeGreaterThanOrEqualTo(0);
        shared.ShouldBeLessThan(secondOnly);
    }

    private static OptimizationSearchSpace Space(int[] counts)
    {
        return new OptimizationSearchSpace(
            counts.Select((count, index) =>
                new OptimizationParameterRange(new PortReference($"p{index:D2}", "value"), min: 0m, count - 1, step: 1m, ValueType.Integer)));
    }

    private static OptimizationTrialResult Result(OptimizationTrial trial, decimal objective)
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
            Executions: 1);

        return OptimizationTrialResult.Evaluate(trial, metrics, OptimizationObjective.InSampleReturnPercent, new OptimizationConstraints());
    }
}