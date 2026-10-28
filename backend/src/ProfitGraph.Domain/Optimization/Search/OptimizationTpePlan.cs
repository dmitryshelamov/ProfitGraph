using System.Numerics;

namespace ProfitGraph.Domain.Optimization.Search;

/// <summary>
///     Version-one deterministic TPE over exact integer grid coordinates, with a frozen model per batch (Profit Fox
///     Designer ADR 0033): a seeded random startup, then batches guided by the best tenth of the evaluated trials.
/// </summary>
public sealed class OptimizationTpePlan
{
    private const int CandidatesPerPick = 24;
    private const int MaximumUniformDraws = 1000;

    private readonly OptimizationSearchSpace _space;
    private readonly int _budget;

    public int StartupCount { get; }
    public int BatchSize { get; }

    private OptimizationTpePlan(OptimizationSearchSpace space, int budget)
    {
        _space = space;
        _budget = budget;
        int startup = budget / 20;
        StartupCount = startup < 20 ? 20 : startup > 200 ? 200 : startup;
        int batch = budget / 50;
        BatchSize = batch < 16 ? 16 : batch > 64 ? 64 : batch;
    }

    public static bool TryCreate(OptimizationSearchSpace space, int budget, out OptimizationTpePlan? plan)
    {
        ArgumentNullException.ThrowIfNull(space);

        OptimizationSearchSpace.ValidateBudget(budget);
        plan = budget >= 50 && space.GridSize > budget ? new OptimizationTpePlan(space, budget) : null;

        return plan is not null;
    }

    public IEnumerable<OptimizationTrial> StartupCandidates(ulong seed)
    {
        return _space.EnumerateCandidates(_budget, seed, forceRandom: true);
    }

    public IEnumerable<OptimizationTrial> GuidedCandidates(
        IEnumerable<OptimizationTrialResult> results,
        IReadOnlySet<BigInteger> visitedGridIndices,
        int batchIndex,
        int nextTrialIndex,
        ulong seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batchIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(nextTrialIndex);
        OptimizationTrialResult[] evaluated = results.Where(result => result.Status != OptimizationTrialStatus.RejectedByTemplate)
            .OrderBy(result => result.Trial.Index)
            .ToArray();
        int goodCount = (evaluated.Length + 9) / 10;
        goodCount = goodCount > 25 ? 25 : goodCount;
        OptimizationTrialResult[] goodResults = OptimizationRanking.Rank(evaluated)
            .Where(result => result.Status == OptimizationTrialStatus.Completed && result.ObjectiveValue.HasValue)
            .Take(goodCount)
            .ToArray();
        HashSet<BigInteger> goodIndices = goodResults.Select(result => result.Trial.GridIndex).ToHashSet();
        IReadOnlyList<BigInteger>[] good = goodResults.Select(result => _space.CoordinatesAt(result.Trial.GridIndex)).ToArray();
        IReadOnlyList<BigInteger>[] bad = evaluated.Where(result => !goodIndices.Contains(result.Trial.GridIndex))
            .Select(result => _space.CoordinatesAt(result.Trial.GridIndex))
            .ToArray();
        BigInteger[] goodBandwidth = Bandwidths(good.Length);
        BigInteger[] badBandwidth = Bandwidths(bad.Length);
        bool useLongCoordinates = _space.Parameters.All(range => range.Count <= long.MaxValue);
        long[]? longCounts = useLongCoordinates ? _space.Parameters.Select(range => (long)range.Count).ToArray() : null;
        long[][]? longGood = useLongCoordinates ? good.Select(point => point.Select(value => (long)value).ToArray()).ToArray() : null;
        long[][]? longBad = useLongCoordinates ? bad.Select(point => point.Select(value => (long)value).ToArray()).ToArray() : null;
        long[]? longGoodBandwidth = useLongCoordinates ? goodBandwidth.Select(value => (long)value).ToArray() : null;
        long[]? longBadBandwidth = useLongCoordinates ? badBandwidth.Select(value => (long)value).ToArray() : null;
        var random = new OptimizationRandom(OptimizationSeeds.Derive(seed, batchIndex));
        var visited = new HashSet<BigInteger>(visitedGridIndices);
        int limit = _space.CandidateLimit(_budget);

        for (int index = nextTrialIndex; index < limit && visited.Count < _space.GridSize; index++)
        {
            BigInteger? best = null;
            double bestGood = 0;
            double bestBad = 0;

            if (good.Length > 0)
            {
                for (var sample = 0; sample < CandidatesPerPick; sample++)
                {
                    var component = (int)random.NextBelow(good.Length + 1);
                    BigInteger candidate = component == good.Length ? random.NextBelow(_space.GridSize) : Sample(good[component], goodBandwidth, random);

                    if (visited.Contains(candidate))
                    {
                        continue;
                    }

                    IReadOnlyList<BigInteger> coordinates = _space.CoordinatesAt(candidate);
                    double l;
                    double g;

                    if (useLongCoordinates)
                    {
                        long[] longCoordinates = coordinates.Select(value => (long)value).ToArray();
                        l = Density(longCoordinates, longGood!, longCounts!, longGoodBandwidth!);
                        g = Density(longCoordinates, longBad!, longCounts!, longBadBandwidth!);
                    }
                    else
                    {
                        l = Density(coordinates, good, goodBandwidth);
                        g = Density(coordinates, bad, badBandwidth);
                    }

                    if (!best.HasValue || l * bestBad > bestGood * g || (l * bestBad == bestGood * g && candidate < best.Value))
                    {
                        best = candidate;
                        bestGood = l;
                        bestBad = g;
                    }
                }
            }

            if (!best.HasValue)
            {
                for (var draw = 0; draw < MaximumUniformDraws; draw++)
                {
                    BigInteger candidate = random.NextBelow(_space.GridSize);

                    if (!visited.Contains(candidate))
                    {
                        best = candidate;

                        break;
                    }
                }
            }

            if (!best.HasValue)
            {
                yield break;
            }

            visited.Add(best.Value);

            yield return _space.TrialAt(index, best.Value, stage: 2);
        }
    }

    public static int BandwidthRoot(int pointCount, int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pointCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, other: 1);
        var root = 1;

        while (true)
        {
            long power = 1;

            for (var exponent = 0; exponent < dimensions + 4 && power < pointCount; exponent++)
            {
                power *= root;
            }

            if (power >= pointCount)
            {
                return root;
            }

            root++;
        }
    }

    public static BigInteger Bandwidth(BigInteger count, int pointCount, int dimensions)
    {
        int divisor = 4 * BandwidthRoot(pointCount, dimensions);
        BigInteger width = (count + divisor - 1) / divisor;

        return width < 1 ? BigInteger.One : width;
    }

    public static double KernelWeight(BigInteger coordinate, BigInteger center, BigInteger count, BigInteger bandwidth)
    {
        BigInteger distance = coordinate >= center ? coordinate - center : center - coordinate;

        if (distance > bandwidth || coordinate < 0 || coordinate >= count)
        {
            return 0;
        }

        BigInteger left = center < bandwidth ? center : bandwidth;
        BigInteger right = count - 1 - center < bandwidth ? count - 1 - center : bandwidth;
        BigInteger total = bandwidth + 1 + SideWeight(left, bandwidth) + SideWeight(right, bandwidth);

        return (double)(bandwidth + 1 - distance) / (double)total;
    }

    private static BigInteger SideWeight(BigInteger length, BigInteger bandwidth)
    {
        return length * (bandwidth + 1) - length * (length + 1) / 2;
    }

    private BigInteger[] Bandwidths(int pointCount)
    {
        return _space.Parameters.Select(range => Bandwidth(range.Count, pointCount, _space.Parameters.Count)).ToArray();
    }

    private double Density(IReadOnlyList<BigInteger> coordinates, IReadOnlyList<BigInteger>[] points, BigInteger[] bandwidths)
    {
        double sum = 1 / (double)_space.GridSize;

        foreach (IReadOnlyList<BigInteger> point in points)
        {
            double kernel = 1;

            for (var dimension = 0; dimension < coordinates.Count; dimension++)
            {
                kernel *= KernelWeight(coordinates[dimension], point[dimension], _space.Parameters[dimension].Count, bandwidths[dimension]);

                if (kernel == 0)
                {
                    break;
                }
            }

            sum += kernel;
        }

        return sum / (points.Length + 1);
    }

    private double Density(long[] coordinates, long[][] points, long[] counts, long[] bandwidths)
    {
        double sum = 1 / (double)_space.GridSize;

        foreach (long[] point in points)
        {
            double kernel = 1;

            for (var dimension = 0; dimension < coordinates.Length; dimension++)
            {
                kernel *= LongKernelWeight(coordinates[dimension], point[dimension], counts[dimension], bandwidths[dimension]);

                if (kernel == 0)
                {
                    break;
                }
            }

            sum += kernel;
        }

        return sum / (points.Length + 1);
    }

    private static double LongKernelWeight(long coordinate, long center, long count, long bandwidth)
    {
        long distance = coordinate >= center ? coordinate - center : center - coordinate;

        if (distance > bandwidth || coordinate < 0 || coordinate >= count)
        {
            return 0;
        }

        long left = center < bandwidth ? center : bandwidth;
        long right = count - 1 - center < bandwidth ? count - 1 - center : bandwidth;

        try
        {
            long total = checked(bandwidth + 1 + LongSideWeight(left, bandwidth) + LongSideWeight(right, bandwidth));

            return (double)(bandwidth + 1 - distance) / total;
        }
        catch (OverflowException)
        {
            return KernelWeight(coordinate, center, count, bandwidth);
        }
    }

    private static long LongSideWeight(long length, long bandwidth)
    {
        return checked(length * (bandwidth + 1) - length * (length + 1) / 2);
    }

    private BigInteger Sample(IReadOnlyList<BigInteger> center, BigInteger[] bandwidths, OptimizationRandom random)
    {
        BigInteger index = 0;

        for (var dimension = 0; dimension < center.Count; dimension++)
        {
            BigInteger count = _space.Parameters[dimension].Count;
            BigInteger h = bandwidths[dimension];
            BigInteger left = center[dimension] < h ? center[dimension] : h;
            BigInteger right = count - 1 - center[dimension] < h ? count - 1 - center[dimension] : h;
            BigInteger leftWeight = SideWeight(left, h);
            BigInteger draw = random.NextBelow(leftWeight + h + 1 + SideWeight(right, h));
            BigInteger low = center[dimension] - left;
            BigInteger high = center[dimension] + right;

            // Exact cumulative integer weights implement the same walk even for enormous decimal ranges.
            while (low < high)
            {
                BigInteger middle = (low + high) / 2;
                BigInteger cumulative;

                if (middle < center[dimension])
                {
                    BigInteger length = middle - (center[dimension] - left) + 1;
                    cumulative = length * (h + 1 - left) + length * (length - 1) / 2;
                }
                else
                {
                    BigInteger length = middle - center[dimension];
                    cumulative = leftWeight + h + 1 + SideWeight(length, h);
                }

                if (draw < cumulative)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            index = index * count + low;
        }

        return index;
    }
}