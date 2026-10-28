using System.Numerics;

namespace ProfitGraph.Domain.Optimization.Search;

/// <summary>
///     Deterministic two-stage search over indices of an exact decimal grid (Profit Fox Designer ADR 0033): a coarse grid
///     of at most a third of the budget, then the unvisited neighbors of its best three trials, nearest first.
/// </summary>
public sealed class OptimizationCoarseToFinePlan
{
    public const int TopCount = 3;
    public const int MaximumRefineCandidates = 1000000;

    private readonly OptimizationSearchSpace _space;

    public IReadOnlyList<BigInteger> Multipliers { get; }
    public IReadOnlyList<BigInteger> CoarseCounts { get; }
    public BigInteger CoarseGridSize { get; }
    public int RefineCandidateUpperBound { get; }

    private OptimizationCoarseToFinePlan(OptimizationSearchSpace space, BigInteger[] multipliers, BigInteger[] counts, BigInteger size, int refineCandidateUpperBound)
    {
        _space = space;
        Multipliers = Array.AsReadOnly(multipliers);
        CoarseCounts = Array.AsReadOnly(counts);
        CoarseGridSize = size;
        RefineCandidateUpperBound = refineCandidateUpperBound;
    }

    public static bool TryCreate(OptimizationSearchSpace space, int budget, out OptimizationCoarseToFinePlan? plan)
    {
        ArgumentNullException.ThrowIfNull(space);

        OptimizationSearchSpace.ValidateBudget(budget);
        plan = null;
        var target = new BigInteger(budget / 3);

        if (target < 2)
        {
            return false;
        }

        BigInteger minimumSize = BigInteger.One;

        foreach (OptimizationParameterRange range in space.Parameters)
        {
            minimumSize *= BigInteger.Min(range.Count, right: 2);

            if (minimumSize > target)
            {
                return false;
            }
        }

        var multipliers = new BigInteger[space.Parameters.Count];
        var counts = new BigInteger[space.Parameters.Count];

        for (var index = 0; index < space.Parameters.Count; index++)
        {
            BigInteger fineCount = space.Parameters[index].Count;
            multipliers[index] = fineCount <= target ? BigInteger.One : DivideCeiling(fineCount - 1, target - 1);
            counts[index] = CoarseCount(fineCount, multipliers[index]);
        }

        BigInteger size = counts.Aggregate(BigInteger.One, (product, count) => product * count);

        while (size > target)
        {
            int selected = -1;

            for (var index = 0; index < counts.Length; index++)
            {
                if (counts[index] > BigInteger.Min(space.Parameters[index].Count, right: 2) && (selected < 0 || counts[index] > counts[selected]))
                {
                    selected = index;
                }
            }

            if (selected < 0)
            {
                return false;
            }

            BigInteger fineCount = space.Parameters[selected].Count;
            multipliers[selected] = DivideCeiling(fineCount - 1, counts[selected] - 2);
            counts[selected] = CoarseCount(fineCount, multipliers[selected]);
            size = counts.Aggregate(BigInteger.One, (product, count) => product * count);
        }

        BigInteger refineBound = TopCount;

        for (var index = 0; index < counts.Length; index++)
        {
            refineBound *= BigInteger.Min(2 * multipliers[index] + 1, space.Parameters[index].Count);

            if (refineBound > MaximumRefineCandidates)
            {
                return false;
            }
        }

        plan = new OptimizationCoarseToFinePlan(space, multipliers, counts, size, (int)refineBound);

        return true;
    }

    public IEnumerable<OptimizationTrial> StageOneCandidates()
    {
        for (var trialIndex = 0; trialIndex < (int)CoarseGridSize; trialIndex++)
        {
            BigInteger remaining = trialIndex;
            BigInteger gridIndex = BigInteger.Zero;
            BigInteger place = BigInteger.One;

            for (int parameter = CoarseCounts.Count - 1; parameter >= 0; parameter--)
            {
                BigInteger coarseIndex = remaining % CoarseCounts[parameter];
                remaining /= CoarseCounts[parameter];
                BigInteger fineCount = _space.Parameters[parameter].Count;
                BigInteger fineIndex = BigInteger.Min(coarseIndex * Multipliers[parameter], fineCount - 1);
                gridIndex += fineIndex * place;
                place *= fineCount;
            }

            yield return _space.TrialAt(trialIndex, gridIndex);
        }
    }

    public IReadOnlyList<OptimizationTrial> StageTwoCandidates(
        IEnumerable<OptimizationTrialResult> stageOneResults,
        IReadOnlySet<BigInteger> visitedGridIndices,
        int nextTrialIndex)
    {
        ArgumentNullException.ThrowIfNull(stageOneResults);
        ArgumentNullException.ThrowIfNull(visitedGridIndices);
        ArgumentOutOfRangeException.ThrowIfNegative(nextTrialIndex);

        OptimizationTrialResult[] centers = OptimizationRanking.Rank(stageOneResults.Where(result => result.Trial.Stage == 1))
            .Where(result => result.Status == OptimizationTrialStatus.Completed)
            .Take(TopCount)
            .ToArray();
        var candidates = new Dictionary<BigInteger, (BigInteger Numerator, BigInteger Denominator, int Owner)>();

        for (var owner = 0; owner < centers.Length; owner++)
        {
            IReadOnlyList<BigInteger> center = _space.CoordinatesAt(centers[owner].Trial.GridIndex);
            EnumerateBox(dimension: 0, BigInteger.Zero, BigInteger.Zero, BigInteger.One, owner, center);
        }

        BigInteger[] ordered = candidates.Keys.ToArray();
        Array.Sort(
            ordered,
            (left, right) =>
            {
                (BigInteger Numerator, BigInteger Denominator, int Owner) first = candidates[left];
                (BigInteger Numerator, BigInteger Denominator, int Owner) second = candidates[right];
                int distance = (first.Numerator * second.Denominator).CompareTo(second.Numerator * first.Denominator);

                if (distance != 0)
                {
                    return distance;
                }

                int owner = first.Owner.CompareTo(second.Owner);

                return owner != 0 ? owner : left.CompareTo(right);
            });

        return Array.AsReadOnly(ordered.Select((gridIndex, offset) => _space.TrialAt(checked(nextTrialIndex + offset), gridIndex, stage: 2)).ToArray());

        void EnumerateBox(int dimension, BigInteger gridIndex, BigInteger numerator, BigInteger denominator, int owner, IReadOnlyList<BigInteger> center)
        {
            if (dimension == center.Count)
            {
                if (visitedGridIndices.Contains(gridIndex))
                {
                    return;
                }

                if (!candidates.TryGetValue(gridIndex, out (BigInteger Numerator, BigInteger Denominator, int Owner) previous) ||
                    numerator * previous.Denominator < previous.Numerator * denominator ||
                    (numerator * previous.Denominator == previous.Numerator * denominator && owner < previous.Owner))
                {
                    candidates[gridIndex] = (numerator, denominator, owner);
                }

                return;
            }

            BigInteger fineCount = _space.Parameters[dimension].Count;
            BigInteger radius = Multipliers[dimension];
            BigInteger low = BigInteger.Max(BigInteger.Zero, center[dimension] - radius);
            BigInteger high = BigInteger.Min(fineCount - 1, center[dimension] + radius);

            for (BigInteger coordinate = low; coordinate <= high; coordinate++)
            {
                BigInteger distance = BigInteger.Abs(coordinate - center[dimension]);
                bool farther = distance * denominator > numerator * radius;
                EnumerateBox(
                    dimension + 1,
                    gridIndex * fineCount + coordinate,
                    farther ? distance : numerator,
                    farther ? radius : denominator,
                    owner,
                    center);
            }
        }
    }

    private static BigInteger CoarseCount(BigInteger fineCount, BigInteger multiplier)
    {
        return fineCount == BigInteger.One ? BigInteger.One : DivideCeiling(fineCount - 1, multiplier) + 1;
    }

    private static BigInteger DivideCeiling(BigInteger numerator, BigInteger denominator)
    {
        return (numerator + denominator - 1) / denominator;
    }
}