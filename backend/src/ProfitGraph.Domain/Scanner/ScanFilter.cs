using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     The part of a preset that decides which pairs it keeps: its conditions and its stale switch.
/// </summary>
public sealed record ScanFilter(IReadOnlyList<ScanCondition> Conditions, bool HideStale)
{
    /// <summary>
    ///     Whether the filter keeps a pair, as the page filters it: every condition holds (bounds inclusive, an empty metric fails) and,
    ///     when asked, the pair has traded in the last 15 minutes. A condition on a metric in <paramref name="unknown" /> is not checked:
    ///     history has no quotes, so a replay cannot know the spread (ADR 0059, section 6).
    /// </summary>
    public bool Keeps(ScanMetrics metrics, IReadOnlySet<ScanMetric>? unknown = null)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return !(HideStale && metrics.Stale) &&
            Conditions.All(condition => unknown?.Contains(condition.Metric) == true || condition.Holds(metrics));
    }
}