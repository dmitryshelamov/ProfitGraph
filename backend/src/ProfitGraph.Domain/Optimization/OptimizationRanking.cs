using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     Best objective first, unavailable objectives last, then the canonical parameter tuple; completion order never
///     breaks a tie (Profit Fox Designer ADR 0033).
/// </summary>
public static class OptimizationRanking
{
    public static IReadOnlyList<OptimizationTrialResult> Rank(IEnumerable<OptimizationTrialResult> results)
    {
        OptimizationTrialResult[] ranking = [.. results];
        Array.Sort(ranking, Compare);

        return Array.AsReadOnly(ranking);
    }

    private static int Compare(OptimizationTrialResult left, OptimizationTrialResult right)
    {
        int objective = Nullable.Compare(right.ObjectiveValue, left.ObjectiveValue);

        if (objective != 0)
        {
            return objective;
        }

        KeyValuePair<PortReference, decimal>[] leftTuple = Tuple(left);
        KeyValuePair<PortReference, decimal>[] rightTuple = Tuple(right);

        for (var index = 0; index < Math.Min(leftTuple.Length, rightTuple.Length); index++)
        {
            int node = StringComparer.Ordinal.Compare(leftTuple[index].Key.NodeId, rightTuple[index].Key.NodeId);
            int key = StringComparer.Ordinal.Compare(leftTuple[index].Key.PortKey, rightTuple[index].Key.PortKey);
            int value = leftTuple[index].Value.CompareTo(rightTuple[index].Value);

            if (node != 0 || key != 0 || value != 0)
            {
                return node != 0 ? node : key != 0 ? key : value;
            }
        }

        return leftTuple.Length.CompareTo(rightTuple.Length);
    }

    private static KeyValuePair<PortReference, decimal>[] Tuple(OptimizationTrialResult result)
    {
        return
        [
            .. result.Trial.Selections.OptimizedValues.OrderBy(pair => pair.Key.NodeId, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.PortKey, StringComparer.Ordinal)
        ];
    }
}