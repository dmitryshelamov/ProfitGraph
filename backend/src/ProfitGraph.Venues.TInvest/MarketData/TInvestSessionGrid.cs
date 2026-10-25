using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     Turns a share's real candles into a complete history of one range (ADR 0023, policy <c>tinvest-reference/v1</c>).
///     An interval is open when the reference share or the share itself traded in it; a run of open intervals is a
///     segment. In a segment where the share traded at least once, its empty intervals become candles without trades at its
///     last earlier real close; every other interval is a closure. Intervals opening on Saturday or Sunday in Moscow time
///     are closures, and their candles are left out. Candles are read padded around the range so that segments crossing
///     its edges are judged whole.
/// </summary>
internal static class TInvestSessionGrid
{
    public const string Policy = "tinvest-reference/v1";

    private static readonly TimeSpan Moscow = TimeSpan.FromHours(3);

    /// <param name="timeframe">The grid.</param>
    /// <param name="from">The first open time of the range.</param>
    /// <param name="to">The end of the range.</param>
    /// <param name="paddedFrom">The first open time read, at or before <paramref name="from" />.</param>
    /// <param name="paddedTo">The end of what was read, at or after <paramref name="to" />.</param>
    /// <param name="share">The share's real candles of the padded range by open time.</param>
    /// <param name="reference">The open times of the reference share's real candles of the padded range.</param>
    /// <param name="priorClose">The share's last real close before <paramref name="paddedFrom" />, if known.</param>
    public static HistoricalCandlesInternal Build(
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset paddedFrom,
        DateTimeOffset paddedTo,
        IReadOnlyDictionary<DateTimeOffset, Candle> share,
        IReadOnlySet<DateTimeOffset> reference,
        decimal? priorClose)
    {
        var times = new List<DateTimeOffset>();

        for (DateTimeOffset time = paddedFrom; time < paddedTo; time += timeframe.Duration)
        {
            times.Add(time);
        }

        bool[] open = [.. times.Select(time => !IsWeekend(time) && (reference.Contains(time) || share.ContainsKey(time)))];
        bool[] admitted = Admitted(times, open, share);
        var candles = new List<Candle>();
        var closures = new List<CandleRangeInternal>();
        decimal? lastClose = priorClose;

        for (var index = 0; index < times.Count; index++)
        {
            DateTimeOffset time = times[index];
            bool inRange = time >= from && time < to;

            if (open[index] && share.TryGetValue(time, out Candle? candle))
            {
                lastClose = candle.Close;

                if (inRange)
                {
                    candles.Add(candle);
                }
            }
            else if (inRange && open[index] && admitted[index] && lastClose is { } close)
            {
                candles.Add(new Candle(time, close, close, close, close, Volume: 0m) { NoTrades = true });
            }
            else if (inRange)
            {
                Close(closures, time, time + timeframe.Duration);
            }
        }

        return new HistoricalCandlesInternal(candles, closures);
    }

    /// <summary>
    ///     Whether an interval opens on a Saturday or Sunday in Moscow time, when MOEX holds only weekend sessions.
    /// </summary>
    public static bool IsWeekend(DateTimeOffset openTime)
    {
        DayOfWeek day = openTime.ToOffset(Moscow).DayOfWeek;

        return day is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    private static bool[] Admitted(List<DateTimeOffset> times, bool[] open, IReadOnlyDictionary<DateTimeOffset, Candle> share)
    {
        var admitted = new bool[times.Count];

        for (var start = 0; start < times.Count;)
        {
            if (!open[start])
            {
                start++;

                continue;
            }

            int end = start;

            while (end < times.Count && open[end])
            {
                end++;
            }

            bool traded = Enumerable.Range(start, end - start).Any(index => share.ContainsKey(times[index]));

            for (int index = start; index < end; index++)
            {
                admitted[index] = traded;
            }

            start = end;
        }

        return admitted;
    }

    private static void Close(List<CandleRangeInternal> closures, DateTimeOffset from, DateTimeOffset to)
    {
        if (closures.Count > 0 && closures[^1].To == from)
        {
            closures[^1] = closures[^1] with { To = to };
        }
        else
        {
            closures.Add(new CandleRangeInternal(from, to));
        }
    }
}