using System.Numerics;

using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Domain.Optimization;

/// <summary>
///     The exact grid and its seeded enumeration are a reproducibility contract; the vectors come from Profit Fox Designer.
/// </summary>
public sealed class OptimizationSearchSpaceTests
{
    [Test]
    public void CandidateEnumerationIsBoundedAndOversizedBudgetsAreRejected()
    {
        var space = new OptimizationSearchSpace(
            [new OptimizationParameterRange(new PortReference("x", "value"), min: 0m, decimal.MaxValue, step: 1m, ValueType.Integer)]);
        OptimizationTrial[] candidates = space.EnumerateCandidates(budget: 3, seed: 42).ToArray();

        candidates.Length.ShouldBe(30);
        candidates.Select(trial => trial.GridIndex).Distinct().Count().ShouldBe(30);
        space.CandidateLimit(OptimizationSearchSpace.MaximumBudget).ShouldBe(OptimizationSearchSpace.MaximumCandidates);
        Should.Throw<OptimizationRuleException>(() => space.Enumerate(OptimizationSearchSpace.MaximumBudget + 1)).Code.ShouldBe(OptimizationRuleException.InvalidBudget);
        Should.Throw<OptimizationRuleException>(() => space.EnumerateCandidates(int.MaxValue, seed: 42));
    }

    [Test]
    public void GraphRangesKeepDeclaredTypesAndSortByNodeAndParameter()
    {
        var graph = new StrategyGraph(
            [
                Node("z", "Constant", new Dictionary<string, ParamValue> { ["value"] = new DecimalOptimizeParamValue(Min: 1m, Max: 2m, Step: 1m) }, ValueType.Integer),
                Node("a", "Constant", new Dictionary<string, ParamValue> { ["value"] = new DecimalOptimizeParamValue(Min: 0.1m, Max: 0.3m, Step: 0.1m) }, ValueType.Price)
            ],
            Array.Empty<GraphConnection>());
        OptimizationSearchSpace space = OptimizationSearchSpace.FromGraph(Compile(graph));
        space.GridSize.ShouldBe(new BigInteger(6));
        space.Parameters.Select(range => range.Type).ShouldBe(new[] { ValueType.Price, ValueType.Integer });
        OptimizationTrial[] trials = space.Enumerate().ToArray();
        trials.Select(trial => trial.Index).ShouldBe(Enumerable.Range(start: 0, count: 6));
        trials.Select(trial => trial.Selections.OptimizedValues[new PortReference("a", "value")]).ShouldBe(new[] { 0.1m, 0.1m, 0.2m, 0.2m, 0.3m, 0.3m });
        trials.Select(trial => trial.Selections.OptimizedValues[new PortReference("z", "value")]).ShouldBe(new[] { 1m, 2m, 1m, 2m, 1m, 2m });
    }

    [Test]
    public void ExactSizeDoesNotRoundUpAtADecimalDivisionBoundaryOrOverflowAtExtremeBounds()
    {
        var boundary = new OptimizationParameterRange(new PortReference("x", "value"), min: 0m, max: 0.9999999999999999999999999999m, step: 0.5m, ValueType.Scalar);
        boundary.Count.ShouldBe(new BigInteger(2));
        boundary.ValueAt(BigInteger.One).ShouldBe(0.5m);
        var huge = new OptimizationParameterRange(new PortReference("x", "value"), decimal.MinValue, decimal.MaxValue, step: 1m, ValueType.Integer);
        huge.Count.ShouldBe((BigInteger)decimal.MaxValue * 2 + 1);
        huge.ValueAt(huge.Count - 1).ShouldBe(decimal.MaxValue);
    }

    [Test]
    public void RangesCannotSilentlyRoundDistinctGridValuesAtDecimalPrecisionLimits()
    {
        Should.Throw<OptimizationRuleException>(() => new OptimizationParameterRange(
            new PortReference("x", "value"),
            min: 0m,
            decimal.MaxValue,
            step: 0.1m,
            ValueType.Scalar));
    }

    [Test]
    public void EmptyDuplicateAndNonIntegralIntegerSpacesAreRejected()
    {
        Should.Throw<OptimizationRuleException>(() => new OptimizationSearchSpace(Array.Empty<OptimizationParameterRange>()));
        Should.Throw<OptimizationRuleException>(() =>
            OptimizationSearchSpace.FromGraph(Compile(new StrategyGraph([Node("fixed", "Constant")], Array.Empty<GraphConnection>()))));
        var range = new OptimizationParameterRange(new PortReference("x", "value"), min: 1m, max: 2m, step: 1m, ValueType.Integer);
        Should.Throw<OptimizationRuleException>(() => new OptimizationSearchSpace([range, range]));
        Should.Throw<OptimizationRuleException>(() => new OptimizationParameterRange(range.Reference, min: 1.5m, max: 2m, step: 1m, ValueType.Integer));
        Should.Throw<OptimizationRuleException>(() => new OptimizationParameterRange(range.Reference, min: 1m, max: 2m, step: 0.5m, ValueType.Integer));
        Should.Throw<OptimizationRuleException>(() => new OptimizationParameterRange(range.Reference, min: 1m, max: 2.5m, step: 1m, ValueType.Integer));
    }

    [Test]
    public void SamplingIsUniqueReproducibleAndWorksBeyondUlongSizedGrids()
    {
        var range = new OptimizationParameterRange(new PortReference("x", "value"), min: 0m, decimal.MaxValue, step: 1m, ValueType.Integer);
        var space = new OptimizationSearchSpace([range]);
        BigInteger[] first = space.Enumerate(budget: 2000, seed: 42).Select(trial => trial.GridIndex).ToArray();
        first.Length.ShouldBe(2000);
        first.Distinct().Count().ShouldBe(2000);
        first.ShouldBe(space.Enumerate(budget: 2000, seed: 42).Select(trial => trial.GridIndex));
        first.SequenceEqual(space.Enumerate(budget: 2000, seed: 43).Select(trial => trial.GridIndex)).ShouldBeFalse();
        first.ShouldAllBe(index => index >= 0 && index < space.GridSize);
        first.Any(index => index > ulong.MaxValue).ShouldBeTrue();
    }

    [Test]
    public void CandidateSamplingVisitsLargeGridWithoutNarrowingItsCardinalityToInt()
    {
        var range = new OptimizationParameterRange(new PortReference("x", "value"), min: 0m, max: 3000000000m, step: 1m, ValueType.Integer);
        var space = new OptimizationSearchSpace([range]);
        OptimizationTrial[] candidates = space.EnumerateCandidates(budget: 3, seed: 42).Take(3).ToArray();
        space.GridSize.ShouldBeGreaterThan(int.MaxValue);
        candidates.Select(trial => trial.Index).ShouldBe(new[] { 0, 1, 2 });
        candidates.Select(trial => trial.GridIndex).Distinct().Count().ShouldBe(3);
        candidates.ShouldAllBe(trial => trial.GridIndex < space.GridSize);
    }

    [Test]
    public void SeededSamplingPinsThePrngAndSparsePermutationContract()
    {
        var space = new OptimizationSearchSpace([new OptimizationParameterRange(new PortReference("x", "value"), min: 0m, max: 99m, step: 1m, ValueType.Integer)]);
        space.Enumerate(budget: 10, seed: 42)
            .Select(trial => trial.GridIndex)
            .ShouldBe(new[] { 21, 4, 84, 23, 10, 98, 42, 92, 54, 72 }.Select(value => new BigInteger(value)));
    }

    [Test]
    public void DecimalScaleDoesNotChangeGridValuesOrSampling()
    {
        var first = new OptimizationSearchSpace([new OptimizationParameterRange(new PortReference("x", "value"), min: 0.1m, max: 10m, step: 0.1m, ValueType.Scalar)]);
        var second = new OptimizationSearchSpace(
            [new OptimizationParameterRange(new PortReference("x", "value"), min: 0.1000m, max: 10.0000m, step: 0.1000m, ValueType.Scalar)]);
        first.GridSize.ShouldBe(second.GridSize);
        first.Enumerate(budget: 10, seed: 42)
            .Select(trial => trial.Selections.OptimizedValues.Single().Value)
            .ShouldBe(second.Enumerate(budget: 10, seed: 42).Select(trial => trial.Selections.OptimizedValues.Single().Value));
    }

    [Test]
    public void SamplingNearFullGridHasNoCollisionsAndBudgetBoundaryUsesFullGrid()
    {
        var space = new OptimizationSearchSpace([new OptimizationParameterRange(new PortReference("x", "value"), min: 0m, max: 99m, step: 1m, ValueType.Integer)]);
        space.Enumerate(budget: 99, seed: 1).Select(trial => trial.GridIndex).Distinct().Count().ShouldBe(99);
        space.Enumerate(budget: 100, seed: 1).Select(trial => trial.GridIndex).ShouldBe(Enumerable.Range(start: 0, count: 100).Select(index => new BigInteger(index)));
        Should.Throw<OptimizationRuleException>(() => space.Enumerate(budget: 0));
    }
}