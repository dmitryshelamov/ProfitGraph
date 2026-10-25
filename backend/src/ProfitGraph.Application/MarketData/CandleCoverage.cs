using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData;

/// <summary>
///     Which grid intervals of a range a history covers: an interval is covered by a candle or by a market closure
///     (ADR 0023, TC1). Closures are compared as ordered half-open ranges.
/// </summary>
internal static class CandleCoverage
{
    /// <summary>
    ///     The ranges of [from, to) that neither a candle nor a closure covers, oldest first.
    /// </summary>
    public static List<CandleRangeInternal> FindMissing(
        IReadOnlySet<DateTimeOffset> candles,
        IReadOnlyList<CandleRangeInternal> closures,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        CandleRangeInternal[] ordered = [.. closures.OrderBy(closure => closure.From)];
        var ranges = new List<CandleRangeInternal>();
        DateTimeOffset? start = null;
        var next = 0;

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            while (next < ordered.Length && ordered[next].To <= time)
            {
                next++;
            }

            bool covered = candles.Contains(time) || (next < ordered.Length && ordered[next].From <= time);

            if (!covered)
            {
                start ??= time;
            }
            else if (start is not null)
            {
                ranges.Add(new CandleRangeInternal(start.Value, time));
                start = null;
            }
        }

        if (start is not null)
        {
            ranges.Add(new CandleRangeInternal(start.Value, to));
        }

        return ranges;
    }

    /// <summary>
    ///     Clips closures to [from, to) and merges overlapping or touching ones, so the result does not depend on how they
    ///     were split into chunks or stored rows.
    /// </summary>
    public static List<CandleRangeInternal> Merge(IEnumerable<CandleRangeInternal> closures, DateTimeOffset from, DateTimeOffset to)
    {
        var merged = new List<CandleRangeInternal>();

        foreach (CandleRangeInternal closure in closures.OrderBy(closure => closure.From))
        {
            DateTimeOffset start = closure.From < from ? from : closure.From;
            DateTimeOffset end = closure.To > to ? to : closure.To;

            if (start >= end)
            {
                continue;
            }

            if (merged.Count > 0 && merged[^1].To >= start)
            {
                merged[^1] = merged[^1] with { To = end > merged[^1].To ? end : merged[^1].To };
            }
            else
            {
                merged.Add(new CandleRangeInternal(start, end));
            }
        }

        return merged;
    }

    /// <summary>
    ///     Whether a closure is a nonempty UTC range on the timeframe grid.
    /// </summary>
    public static bool IsValid(CandleRangeInternal closure, Timeframe timeframe)
    {
        return closure.From.Offset == TimeSpan.Zero && closure.To.Offset == TimeSpan.Zero && closure.From < closure.To &&
            timeframe.IsAligned(closure.From) && timeframe.IsAligned(closure.To);
    }

    /// <summary>
    ///     Whether any of the times lies inside one of the closures.
    /// </summary>
    public static bool Overlaps(IEnumerable<DateTimeOffset> times, IReadOnlyList<CandleRangeInternal> closures)
    {
        return times.Any(time => closures.Any(closure => closure.From <= time && time < closure.To));
    }
}