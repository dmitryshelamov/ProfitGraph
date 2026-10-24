using System.Numerics;

using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Domain.Optimization;

public sealed class OptimizationTpePlanTests
{
    [TestCase(arg1: 1, arg2: 3, arg3: 1)]
    [TestCase(arg1: 25, arg2: 3, arg3: 2)]
    [TestCase(arg1: 200, arg2: 3, arg3: 3)]
    [TestCase(arg1: 1, arg2: 1, arg3: 1)]
    [TestCase(arg1: 25, arg2: 1, arg3: 2)]
    [TestCase(arg1: 200, arg2: 1, arg3: 3)]
    public void BandwidthRootAndCeilingMatchIntegerContract(int points, int dimensions, int root)
    {
        OptimizationTpePlan.BandwidthRoot(points, dimensions).ShouldBe(root);
        OptimizationTpePlan.Bandwidth(count: 29, points, dimensions).ShouldBe(new BigInteger((29 + 4 * root - 1) / (4 * root)));
        OptimizationTpePlan.Bandwidth(count: 1, points, dimensions).ShouldBe(BigInteger.One);
    }

    [Test]
    public void TruncatedKernelsSumToOneAndInteriorIsSymmetric()
    {
        for (var center = 0; center < 11; center++)
        {
            double sum = Enumerable.Range(start: 0, count: 11).Sum(x => OptimizationTpePlan.KernelWeight(x, center, count: 11, bandwidth: 3));
            sum.ShouldBe(expected: 1d, tolerance: 0.000000000001d);
        }

        for (var offset = 0; offset <= 3; offset++)
        {
            OptimizationTpePlan.KernelWeight(5 - offset, center: 5, count: 11, bandwidth: 3)
                .ShouldBe(OptimizationTpePlan.KernelWeight(5 + offset, center: 5, count: 11, bandwidth: 3));
        }
    }

    [TestCase(arg1: 49, arg2: 100, arg3: false)]
    [TestCase(arg1: 50, arg2: 50, arg3: false)]
    [TestCase(arg1: 50, arg2: 51, arg3: true)]
    public void ApplicabilityRequiresLearningBudgetAndLargerGrid(int budget, int count, bool expected)
    {
        OptimizationTpePlan.TryCreate(Space([count]), budget, out _).ShouldBe(expected);
    }

    [Test]
    public void BatchIsRepeatableIgnoresResultOrderAndAvoidsVisitedIndices()
    {
        OptimizationSearchSpace space = Space([17, 29, 29]);
        OptimizationTpePlan.TryCreate(space, budget: 300, out OptimizationTpePlan? plan).ShouldBeTrue();
        plan.ShouldNotBeNull();
        OptimizationTrialResult[] results = plan.StartupCandidates(seed: 7).Take(plan.StartupCount).Select(Result).ToArray();
        HashSet<BigInteger> visited = results.Select(result => result.Trial.GridIndex).ToHashSet();
        visited.Add(0);
        OptimizationTrial[] first = plan.GuidedCandidates(results, visited, batchIndex: 0, nextTrialIndex: 20, seed: 7).Take(plan.BatchSize).ToArray();
        OptimizationTrial[] second = plan.GuidedCandidates(results.Reverse(), visited, batchIndex: 0, nextTrialIndex: 20, seed: 7).Take(plan.BatchSize).ToArray();
        first.Select(trial => trial.GridIndex).ShouldBe(second.Select(trial => trial.GridIndex));
        first.Select(trial => trial.GridIndex).Distinct().Count().ShouldBe(plan.BatchSize);
        first.ShouldAllBe(trial => !visited.Contains(trial.GridIndex) && trial.Stage == 2);
        first.Select(trial => trial.Index).ShouldBe(Enumerable.Range(start: 20, plan.BatchSize));
    }

    [Test]
    public void NoEligibleResultsUseDeterministicUniformFallback()
    {
        OptimizationSearchSpace space = Space([101]);
        OptimizationTpePlan.TryCreate(space, budget: 50, out OptimizationTpePlan? plan).ShouldBeTrue();
        plan.ShouldNotBeNull();
        OptimizationTrialResult[] failed = plan.StartupCandidates(seed: 1)
            .Take(plan.StartupCount)
            .Select(trial => OptimizationTrialResult.Failed(trial, "synthetic_failure"))
            .ToArray();
        HashSet<BigInteger> visited = failed.Select(result => result.Trial.GridIndex).ToHashSet();
        OptimizationTrial[] batch = plan.GuidedCandidates(failed, visited, batchIndex: 0, nextTrialIndex: 20, seed: 1).Take(16).ToArray();
        batch.Length.ShouldBe(16);
        batch.ShouldAllBe(trial => !visited.Contains(trial.GridIndex));
        batch.Select(trial => trial.GridIndex)
            .ShouldBe(plan.GuidedCandidates(failed.Reverse(), visited, batchIndex: 0, nextTrialIndex: 20, seed: 1).Take(16).Select(trial => trial.GridIndex));
    }

    [Test]
    public void SyntheticSmoothThreePeakBenchmark()
    {
        OptimizationSearchSpace space = Space([17, 29, 29]);
        OptimizationTpePlan.TryCreate(space, budget: 300, out OptimizationTpePlan? plan).ShouldBeTrue();
        plan.ShouldNotBeNull();
        decimal[] grid = Enumerable.Range(start: 0, (int)space.GridSize).Select(index => Objective(space.CoordinatesAt(index))).ToArray();
        decimal maximum = grid.Max();
        var maxima = 0;
        var neverWorse = true;

        for (ulong seed = 1; seed <= 10; seed++)
        {
            List<OptimizationTrialResult> results = plan.StartupCandidates(seed).Take(plan.StartupCount).Select(Result).ToList();
            var batchIndex = 0;

            while (results.Count < 300)
            {
                HashSet<BigInteger> visited = results.Select(result => result.Trial.GridIndex).ToHashSet();
                OptimizationTrial[] batch = plan.GuidedCandidates(results, visited, batchIndex++, results.Count, seed)
                    .Take(300 - results.Count < plan.BatchSize ? 300 - results.Count : plan.BatchSize)
                    .ToArray();
                batch.Length.ShouldBeGreaterThan(0);
                results.AddRange(batch.Select(Result));
            }

            decimal tpe = results.Max(result => result.ObjectiveValue!.Value);
            decimal random = space.Enumerate(budget: 300, seed, forceRandom: true).Max(trial => Objective(space.CoordinatesAt(trial.GridIndex)));
            int tpeRank = 1 + grid.Count(value => value > tpe);
            int randomRank = 1 + grid.Count(value => value > random);
            TestContext.Out.WriteLine($"seed={seed}: TPE={tpe} rank={tpeRank}; random={random} rank={randomRank}; global={maximum}");
            neverWorse &= tpe >= random;
            maxima += tpe == maximum ? 1 : 0;
        }

        TestContext.Out.WriteLine($"global maximum reached: {maxima}/10; at least random for every seed: {neverWorse}");
        neverWorse.ShouldBeTrue();
        maxima.ShouldBeGreaterThanOrEqualTo(8);
    }

    private static decimal Objective(IReadOnlyList<BigInteger> coordinates)
    {
        var x = (decimal)coordinates[0];
        var y = (decimal)coordinates[1];
        var z = (decimal)coordinates[2];

        return Peak(x - 8, y - 14, z - 14, amplitude: 100m) + Peak(x - 2, y - 5, z - 23, amplitude: 30m) + Peak(x - 14, y - 24, z - 5, amplitude: 25m);
    }

    private static decimal Peak(decimal x, decimal y, decimal z, decimal amplitude)
    {
        return amplitude / (1m + x * x / 4m + y * y / 9m + z * z / 9m);
    }

    private static OptimizationSearchSpace Space(int[] counts)
    {
        return new OptimizationSearchSpace(
            counts.Select((count, index) =>
                new OptimizationParameterRange(new PortReference($"p{index:D2}", "value"), min: 0m, count - 1, step: 1m, ValueType.Integer)));
    }

    private static OptimizationTrialResult Result(OptimizationTrial trial)
    {
        // Canonical coordinates have integer parameter values in this synthetic fixture.
        BigInteger[] coordinates = trial.Selections.OptimizedValues.OrderBy(pair => pair.Key.NodeId, StringComparer.Ordinal)
            .Select(pair => new BigInteger(pair.Value))
            .ToArray();

        return OptimizationTrialResult.Restore(trial, metrics: null, OptimizationTrialStatus.Completed, Objective(coordinates), failureCode: null, elapsedMs: 0);
    }
}