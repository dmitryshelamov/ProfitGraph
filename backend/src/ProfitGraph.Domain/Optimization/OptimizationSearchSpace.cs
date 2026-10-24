using System.Numerics;

using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     The Cartesian grid of exact parameter ranges (Profit Fox Designer ADR 0033). Parameters are in ordinal order of
///     node and key; a grid index is mixed-radix with the last parameter fastest. A grid within the budget is visited in
///     full and in order; a larger one, or an explicit random search, draws distinct indices with a seeded sparse
///     partial Fisher–Yates shuffle. Candidate indices never depend on worker scheduling.
/// </summary>
public sealed class OptimizationSearchSpace
{
    public const int DefaultBudget = 2000;
    public const int MaximumBudget = 1_000_000;
    public const int CandidateMultiplier = 10;

    /// <summary>
    ///     Sampling keeps one swap per inspected candidate, so inspection stays bounded even for the largest budget.
    /// </summary>
    public const int MaximumCandidates = 2_000_000;

    public IReadOnlyList<OptimizationParameterRange> Parameters { get; }
    public BigInteger GridSize { get; }

    public OptimizationSearchSpace(IEnumerable<OptimizationParameterRange> parameters)
    {
        OptimizationParameterRange[] ranges =
        [
            .. parameters.OrderBy(parameter => parameter.Reference.NodeId, StringComparer.Ordinal)
                .ThenBy(parameter => parameter.Reference.PortKey, StringComparer.Ordinal)
        ];

        if (ranges.Length == 0)
        {
            throw new OptimizationRuleException(OptimizationRuleException.InvalidSearchSpace, "Optimization needs at least one range.");
        }

        if (ranges.Select(range => range.Reference).Distinct().Count() != ranges.Length)
        {
            throw new OptimizationRuleException(OptimizationRuleException.InvalidSearchSpace, "Each parameter can have one range only.");
        }

        Parameters = Array.AsReadOnly(ranges);
        GridSize = ranges.Aggregate(BigInteger.One, (size, range) => size * range.Count);
    }

    /// <summary>
    ///     The ranges of a compiled graph, keyed by each parameter's external identity. A wired input cannot be optimized.
    /// </summary>
    public static OptimizationSearchSpace FromGraph(CompiledTradingGraph graph)
    {
        var ranges = new List<OptimizationParameterRange>();

        foreach (CompiledTradingNode node in graph.Nodes)
        {
            foreach ((string key, ParamValue value) in node.Node.Parameters)
            {
                if (value is not DecimalOptimizeParamValue range)
                {
                    continue;
                }

                CompiledEngineInput? input = node.EngineInputs.SingleOrDefault(binding => binding.Spec.Key == key);

                if (input?.Source is not null)
                {
                    throw new OptimizationRuleException(OptimizationRuleException.InvalidSearchSpace, "A wired input cannot be optimized.");
                }

                ValueType type = input?.Spec.ValueType ?? (key == "period" ? ValueType.Integer : node.Node.DeclaredValueType ?? ValueType.Scalar);
                ranges.Add(new OptimizationParameterRange(node.Node.ParameterReference(key), range.Min, range.Max, range.Step, type));
            }
        }

        return new OptimizationSearchSpace(ranges.DistinctBy(range => range.Reference));
    }

    /// <summary>
    ///     Up to <paramref name="budget" /> candidates: the whole grid in order when it fits, otherwise a seeded sample.
    /// </summary>
    public IEnumerable<OptimizationTrial> Enumerate(int budget = DefaultBudget, ulong seed = 0, bool forceRandom = false)
    {
        ValidateBudget(budget);

        return EnumerateCore((int)BigInteger.Min(GridSize, budget), budget, seed, forceRandom);
    }

    /// <summary>
    ///     Candidates up to <see cref="CandidateLimit" />, so candidates a template rejects do not spend the trial budget.
    ///     Trial indices count visited candidates, not positions in the possibly much larger grid.
    /// </summary>
    public IEnumerable<OptimizationTrial> EnumerateCandidates(int budget, ulong seed, bool forceRandom = false)
    {
        ValidateBudget(budget);

        return EnumerateCore(CandidateLimit(budget), budget, seed, forceRandom);
    }

    public int CandidateLimit(int budget)
    {
        ValidateBudget(budget);

        return (int)BigInteger.Min(GridSize, Math.Min((long)budget * CandidateMultiplier, MaximumCandidates));
    }

    public IReadOnlyList<BigInteger> CoordinatesAt(BigInteger gridIndex)
    {
        if (gridIndex < BigInteger.Zero || gridIndex >= GridSize)
        {
            throw new ArgumentOutOfRangeException(nameof(gridIndex));
        }

        var coordinates = new BigInteger[Parameters.Count];

        for (int parameter = Parameters.Count - 1; parameter >= 0; parameter--)
        {
            coordinates[parameter] = gridIndex % Parameters[parameter].Count;
            gridIndex /= Parameters[parameter].Count;
        }

        return coordinates;
    }

    public OptimizationTrial TrialAt(int index, BigInteger gridIndex, int stage = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        if (stage is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        return new OptimizationTrial(index, gridIndex, Selections(gridIndex), stage);
    }

    internal static void ValidateBudget(int budget)
    {
        if (budget is < 1 or > MaximumBudget)
        {
            throw new OptimizationRuleException(OptimizationRuleException.InvalidBudget, $"The budget must be between 1 and {MaximumBudget}.");
        }
    }

    private EngineExecutionInputs Selections(BigInteger gridIndex)
    {
        IReadOnlyList<BigInteger> coordinates = CoordinatesAt(gridIndex);
        var values = new Dictionary<PortReference, decimal>();

        for (int parameter = Parameters.Count - 1; parameter >= 0; parameter--)
        {
            OptimizationParameterRange range = Parameters[parameter];
            values.Add(range.Reference, range.ValueAt(coordinates[parameter]));
        }

        return new EngineExecutionInputs(values);
    }

    private IEnumerable<OptimizationTrial> EnumerateCore(int count, int budget, ulong seed, bool forceRandom)
    {
        var random = new OptimizationRandom(seed);
        var swaps = new Dictionary<BigInteger, BigInteger>();
        bool sampled = forceRandom || GridSize > budget;

        for (var index = 0; index < count; index++)
        {
            BigInteger gridIndex = index;

            if (sampled)
            {
                // Sparse partial Fisher–Yates: a uniform choice among the remaining indices in memory proportional to the visits.
                BigInteger selected = index + random.NextBelow(GridSize - index);
                gridIndex = swaps.GetValueOrDefault(selected, selected);
                swaps[selected] = swaps.GetValueOrDefault(index, index);
                swaps.Remove(index);
            }

            yield return new OptimizationTrial(index, gridIndex, Selections(gridIndex));
        }
    }
}