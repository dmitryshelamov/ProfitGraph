namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The scanner's signals over history (ADR 0059, section 6): a replay is checked at the end of every minute of a period, a pair
///     signals when it enters the result, at most once per cooldown, as the live alerts do, and the first minute only records the
///     result. Each signal enters at the next minute's open; its changes run to the closes 15 minutes, an hour, four hours and a day
///     later, and its largest rise and drop to the highs and lows of the next day. Deterministic for identical inputs.
/// </summary>
public static class ScanStudy
{
    private const decimal Percent = 100m;
    private const int MinutesPerDay = 1440;

    public static IReadOnlyList<ScanSignal> Signals(
        string symbol,
        ScanReplay replay,
        Func<ScanMetrics, bool> keeps,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan cooldown)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(keeps);

        List<ScanSignal> signals = [];
        bool? previous = null;
        DateTimeOffset? lastSignal = null;

        for (var index = 0; index < replay.Count; index++)
        {
            DateTimeOffset at = replay.EndOf(index);

            if (at < from || at >= to)
            {
                continue;
            }

            ScanMetrics metrics = replay.At(index);
            bool kept = keeps(metrics);

            if (kept && previous == false && (lastSignal is not { } last || at - last >= cooldown))
            {
                signals.Add(Signal(symbol, replay, index, metrics));
                lastSignal = at;
            }

            previous = kept;
        }

        return signals;
    }

    public static ScanStudySummary Summarize(IReadOnlyList<ScanSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        return new ScanStudySummary(
            signals.Count,
            Outcome(signals, signal => signal.Change15m),
            Outcome(signals, signal => signal.Change1h),
            Outcome(signals, signal => signal.Change4h),
            Outcome(signals, signal => signal.Change24h),
            Outcome(signals, signal => signal.MaxRise24h),
            Outcome(signals, signal => signal.MaxDrop24h));
    }

    private static ScanSignal Signal(string symbol, ScanReplay replay, int index, ScanMetrics metrics)
    {
        IReadOnlyList<ScanCandle> minutes = replay.Minutes;
        decimal? entry = index + 1 < minutes.Count ? minutes[index + 1].Open : null;

        return new ScanSignal(
            symbol,
            replay.EndOf(index),
            entry,
            metrics,
            Change(minutes, index, entry, minutes: 15),
            Change(minutes, index, entry, minutes: 60),
            Change(minutes, index, entry, minutes: 240),
            Change(minutes, index, entry, MinutesPerDay),
            Extreme(minutes, index, entry, highest: true),
            Extreme(minutes, index, entry, highest: false));
    }

    private static decimal? Change(IReadOnlyList<ScanCandle> candles, int index, decimal? entry, int minutes)
    {
        return entry is { } price && index + minutes < candles.Count ? (candles[index + minutes].Close - price) / price * Percent : null;
    }

    private static decimal? Extreme(IReadOnlyList<ScanCandle> candles, int index, decimal? entry, bool highest)
    {
        if (entry is not { } price || index + MinutesPerDay >= candles.Count)
        {
            return null;
        }

        decimal extreme = highest ? candles[index + 1].High : candles[index + 1].Low;

        for (int i = index + 2; i <= index + MinutesPerDay; i++)
        {
            extreme = highest ? Math.Max(extreme, candles[i].High) : Math.Min(extreme, candles[i].Low);
        }

        return (extreme - price) / price * Percent;
    }

    private static ScanOutcomeSummary Outcome(IReadOnlyList<ScanSignal> signals, Func<ScanSignal, decimal?> value)
    {
        decimal[] values = [.. signals.Select(value).OfType<decimal>().Order()];

        if (values.Length == 0)
        {
            return new ScanOutcomeSummary(Count: 0, RisingShare: null, Mean: null, Median: null);
        }

        int middle = values.Length / 2;
        decimal median = values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2m;

        return new ScanOutcomeSummary(values.Length, (decimal)values.Count(item => item > 0) / values.Length, values.Sum() / values.Length, median);
    }
}