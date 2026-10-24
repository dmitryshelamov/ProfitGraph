using System.Numerics;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     Scores evaluated neighbors within one search using exact grid coordinates and decimal objectives.
/// </summary>
public static class OptimizationNeighborhoodScores
{
    public static IReadOnlyDictionary<int, OptimizationNeighborhoodScore> Calculate(
        OptimizationSearchSpace space,
        IEnumerable<OptimizationTrialResult> results,
        IReadOnlyList<BigInteger>? radii = null)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(results);
        radii ??= Enumerable.Repeat(BigInteger.One, space.Parameters.Count).ToArray();

        if (radii.Count != space.Parameters.Count || radii.Any(radius => radius < BigInteger.One))
        {
            throw new ArgumentException("Neighborhood radii must be positive and match the search dimensions.", nameof(radii));
        }

        OptimizationTrialResult[] evaluated = results.Where(result => result.Status != OptimizationTrialStatus.RejectedByTemplate).ToArray();
        IReadOnlyList<BigInteger>[] coordinates = evaluated.Select(result => space.CoordinatesAt(result.Trial.GridIndex)).ToArray();
        Dictionary<BigInteger, int> byGridIndex =
            evaluated.Select((result, index) => (result, index)).ToDictionary(item => item.result.Trial.GridIndex, item => item.index);
        var scores = new Dictionary<int, OptimizationNeighborhoodScore>();
        BigInteger boxSize = BigInteger.One;

        for (var dimension = 0; dimension < radii.Count; dimension++)
        {
            boxSize *= BigInteger.Min(2 * radii[dimension] + 1, space.Parameters[dimension].Count);
        }

        for (var index = 0; index < evaluated.Length; index++)
        {
            var neighbors = new List<int>();

            if (boxSize < evaluated.Length)
            {
                VisitBox(dimension: 0, BigInteger.Zero, index, neighbors);
            }
            else
            {
                for (var other = 0; other < evaluated.Length; other++)
                {
                    if (other == index)
                    {
                        continue;
                    }

                    var inside = true;

                    for (var dimension = 0; dimension < radii.Count; dimension++)
                    {
                        if (BigInteger.Abs(coordinates[index][dimension] - coordinates[other][dimension]) > radii[dimension])
                        {
                            inside = false;

                            break;
                        }
                    }

                    if (inside)
                    {
                        neighbors.Add(other);
                    }
                }
            }

            var eligible = 0;
            decimal? mean = null;

            try
            {
                var sum = 0m;

                foreach (int other in neighbors.OrderBy(other => evaluated[other].Trial.GridIndex))
                {
                    if (evaluated[other].Status != OptimizationTrialStatus.Completed || evaluated[other].ObjectiveValue is not { } objective)
                    {
                        continue;
                    }

                    eligible++;
                    sum = checked(sum + objective);
                }

                if (eligible > 0)
                {
                    mean = sum / eligible;
                }
            }
            catch (OverflowException)
            {
                // An unrepresentable mean is unavailable; neighbor counts remain observable.
                eligible = neighbors.Count(other => evaluated[other].Status == OptimizationTrialStatus.Completed && evaluated[other].ObjectiveValue is not null);
            }

            scores.Add(evaluated[index].Trial.Index, new OptimizationNeighborhoodScore(neighbors.Count, eligible, mean));
        }

        return scores;

        void VisitBox(int dimension, BigInteger gridIndex, int index, List<int> neighbors)
        {
            if (dimension == radii.Count)
            {
                if (byGridIndex.TryGetValue(gridIndex, out int other) && other != index)
                {
                    neighbors.Add(other);
                }

                return;
            }

            BigInteger count = space.Parameters[dimension].Count;
            BigInteger low = BigInteger.Max(BigInteger.Zero, coordinates[index][dimension] - radii[dimension]);
            BigInteger high = BigInteger.Min(count - 1, coordinates[index][dimension] + radii[dimension]);

            for (BigInteger coordinate = low; coordinate <= high; coordinate++)
            {
                VisitBox(dimension + 1, gridIndex * count + coordinate, index, neighbors);
            }
        }
    }
}