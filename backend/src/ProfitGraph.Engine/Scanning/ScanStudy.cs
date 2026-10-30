using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The scanner's signals over history (ADR 0059, section 6): a replay is checked at the end of every minute of a period, a pair
///     signals when it enters the result, at most once per cooldown, as the live alerts do, and the first minute only records the
///     result. Each signal enters at the next minute's open; its changes run to the closes 15 minutes, an hour, four hours and a day
///     later, and its largest rise and drop to the highs and lows of the next day. Its trade scenarios (section 8) run on the same
///     minutes: a trade leaves at its stop, its target or 24 hours after its entry at that minute's close; a minute that touches both
///     stop and target counts as the stop, and a minute that opens beyond a level leaves at its open. Results are after an assumed
///     0.1 % fee on each side. Where shorts are allowed (ADR 0061) the mirror scenarios of <see cref="ScanTradeIdeas" /> run too: a
///     short leaves at a high at its stop or a low at its target. Deterministic for identical inputs.
/// </summary>
public static class ScanStudy
{
    private const decimal Percent = 100m;
    private const int MinutesPerDay = 1440;
    private const int MinutesPerHour = 60;
    private const decimal FeeRate = 0.001m;
    private const decimal MinimumRewardRisk = 2m;

    public static IReadOnlyList<ScanSignal> Signals(
        string symbol,
        ScanReplay replay,
        Func<ScanMetrics, bool> keeps,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan cooldown,
        bool shorts = false)
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
                signals.Add(Signal(symbol, replay, index, metrics, shorts));
                lastSignal = at;
            }

            previous = kept;
        }

        return signals;
    }

    public static ScanStudySummary Summarize(IReadOnlyList<ScanSignal> signals, bool shorts = false)
    {
        ArgumentNullException.ThrowIfNull(signals);

        return new ScanStudySummary(
            signals.Count,
            Outcome(signals, signal => signal.Change15m),
            Outcome(signals, signal => signal.Change1h),
            Outcome(signals, signal => signal.Change4h),
            Outcome(signals, signal => signal.Change24h),
            Outcome(signals, signal => signal.MaxRise24h),
            Outcome(signals, signal => signal.MaxDrop24h),
            [
                .. Enum.GetValues<ScanScenario>().Select(scenario => Scenario(signals, scenario, PositionDirection.Long)),
                .. shorts ? Enum.GetValues<ScanScenario>().Select(scenario => Scenario(signals, scenario, PositionDirection.Short)) : []
            ]);
    }

    private static ScanSignal Signal(string symbol, ScanReplay replay, int index, ScanMetrics metrics, bool shorts)
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
            Extreme(minutes, index, entry, highest: false),
            Trades(minutes, index, metrics, shorts, ScanLevels.AverageRange(replay.HoursAt(index))));
    }

    /// <summary>
    ///     The scenarios of a signal at the minute at <paramref name="index" />; a stop at a level lies beyond it by the hours' average
    ///     range, as on a card (ADR 0070).
    /// </summary>
    internal static List<ScanTrade> Trades(
        IReadOnlyList<ScanCandle> minutes,
        int index,
        ScanMetrics metrics,
        bool shorts = false,
        decimal? averageRange = null)
    {
        List<ScanTrade> trades = [];

        if (index + 1 >= minutes.Count)
        {
            return trades;
        }

        decimal open = minutes[index + 1].Open;

        if (metrics.Support is { } level && level < open && ScanTradeIdeas.LevelStop(open, level, averageRange) is { } support)
        {
            if (metrics.Resistance is { } resistance && resistance > open &&
                Run(minutes, index + 1, open, support, ScanTradeIdeas.LevelTarget(open, resistance), ScanScenario.Levels, fillMinute: false) is { } levels)
            {
                trades.Add(levels);

                if ((resistance - open) / (open - support) >= MinimumRewardRisk)
                {
                    trades.Add(levels with { Scenario = ScanScenario.LevelsRewardRisk2 });
                }
            }

            if (Run(minutes, index + 1, open, support, open + 2 * (open - support), ScanScenario.Target2R, fillMinute: false) is { } target)
            {
                trades.Add(target);
            }
        }

        if (Pullback(minutes, index) is { } pullback)
        {
            trades.Add(pullback);
        }

        if (shorts)
        {
            trades.AddRange(ShortTrades(minutes, index, metrics, open, averageRange));
        }

        return trades;
    }

    private static IEnumerable<ScanTrade> ShortTrades(IReadOnlyList<ScanCandle> minutes, int index, ScanMetrics metrics, decimal open, decimal? averageRange)
    {
        if (metrics.Resistance is { } level && level > open && ScanTradeIdeas.LevelStop(open, level, averageRange) is { } resistance)
        {
            if (metrics.Support is { } support && support > 0m && support < open &&
                Run(minutes, index + 1, open, resistance, ScanTradeIdeas.LevelTarget(open, support), ScanScenario.Levels, fillMinute: false, PositionDirection.Short) is { } levels)
            {
                yield return levels;

                if ((open - support) / (resistance - open) >= MinimumRewardRisk)
                {
                    yield return levels with { Scenario = ScanScenario.LevelsRewardRisk2 };
                }
            }

            decimal twoRisks = open - 2 * (resistance - open);

            if (twoRisks > 0m && Run(minutes, index + 1, open, resistance, twoRisks, ScanScenario.Target2R, fillMinute: false, PositionDirection.Short) is { } target)
            {
                yield return target;
            }
        }

        if (Rally(minutes, index) is { } rally)
        {
            yield return rally;
        }
    }

    /// <summary>
    ///     A short limit halfway up the last hour's fall (ADR 0061), filled by the first minute within 4 hours that trades at it (at its
    ///     open when it opens above), with the stop at the hour's highest high; a minute that opens at the stop or above never fills.
    /// </summary>
    private static ScanTrade? Rally(IReadOnlyList<ScanCandle> minutes, int index)
    {
        if (index < MinutesPerHour - 1)
        {
            return null;
        }

        decimal high = minutes[index].High;

        for (int i = index - MinutesPerHour + 1; i < index; i++)
        {
            high = Math.Max(high, minutes[i].High);
        }

        decimal close = minutes[index].Close;

        if (high <= close)
        {
            return null;
        }

        decimal limit = ScanTradeIdeas.RallyLimit(close, high);

        for (int i = index + 1; i < minutes.Count && i <= index + (int)ScanTradeIdeas.PullbackWait.TotalMinutes; i++)
        {
            if (minutes[i].High < limit)
            {
                continue;
            }

            decimal fill = Math.Max(limit, minutes[i].Open);
            decimal target = fill - 2 * (high - fill);

            return fill >= high || target <= 0m
                ? null
                : Run(minutes, i, fill, high, target, ScanScenario.Pullback, fillMinute: true, PositionDirection.Short);
        }

        return null;
    }

    /// <summary>
    ///     A limit halfway down the last hour's rise, filled by the first minute within 4 hours that trades at it (at its open when it
    ///     opens below), with the stop at the hour's lowest low; a minute that opens at the stop or below never fills.
    /// </summary>
    private static ScanTrade? Pullback(IReadOnlyList<ScanCandle> minutes, int index)
    {
        if (index < MinutesPerHour - 1)
        {
            return null;
        }

        decimal low = minutes[index].Low;

        for (int i = index - MinutesPerHour + 1; i < index; i++)
        {
            low = Math.Min(low, minutes[i].Low);
        }

        decimal close = minutes[index].Close;

        if (low >= close)
        {
            return null;
        }

        decimal limit = ScanTradeIdeas.PullbackLimit(close, low);

        for (int i = index + 1; i < minutes.Count && i <= index + (int)ScanTradeIdeas.PullbackWait.TotalMinutes; i++)
        {
            if (minutes[i].Low > limit)
            {
                continue;
            }

            decimal fill = Math.Min(limit, minutes[i].Open);

            return fill <= low ? null : Run(minutes, i, fill, low, fill + 2 * (fill - low), ScanScenario.Pullback, fillMinute: true);
        }

        return null;
    }

    /// <summary>
    ///     Follows a trade from the minute at <paramref name="start" />; null when the history ends before it closes. In the minute a
    ///     limit fills, only the stop counts, since its high may have come before the fill.
    /// </summary>
    private static ScanTrade? Run(
        IReadOnlyList<ScanCandle> minutes,
        int start,
        decimal entry,
        decimal stop,
        decimal target,
        ScanScenario scenario,
        bool fillMinute,
        PositionDirection direction = PositionDirection.Long)
    {
        bool @short = direction == PositionDirection.Short;

        for (int i = start; i < start + MinutesPerDay; i++)
        {
            if (i >= minutes.Count)
            {
                return null;
            }

            ScanCandle minute = minutes[i];
            bool first = i == start;

            if (@short ? minute.High >= stop : minute.Low <= stop)
            {
                bool gapped = !first && (@short ? minute.Open > stop : minute.Open < stop);

                return Trade(scenario, entry, stop, target, minutes[start], minute, gapped ? minute.Open : stop, ScanTradeExit.Stop, direction);
            }

            if (!(first && fillMinute) && (@short ? minute.Low <= target : minute.High >= target))
            {
                bool gapped = !first && (@short ? minute.Open < target : minute.Open > target);

                return Trade(scenario, entry, stop, target, minutes[start], minute, gapped ? minute.Open : target, ScanTradeExit.Target, direction);
            }
        }

        ScanCandle last = minutes[start + MinutesPerDay - 1];

        return Trade(scenario, entry, stop, target, minutes[start], last, last.Close, ScanTradeExit.Time, direction);
    }

    private static ScanTrade Trade(
        ScanScenario scenario,
        decimal entry,
        decimal stop,
        decimal target,
        ScanCandle entered,
        ScanCandle exited,
        decimal exit,
        ScanTradeExit reason,
        PositionDirection direction)
    {
        decimal moved = direction == PositionDirection.Long ? exit - entry : entry - exit;
        decimal net = moved - (entry + exit) * FeeRate;

        return new ScanTrade(
            scenario,
            entry,
            stop,
            target,
            entered.OpenTime,
            exited.OpenTime + TimeSpan.FromMinutes(1),
            exit,
            reason,
            net / Math.Abs(entry - stop),
            net / entry * Percent,
            direction);
    }

    private static ScanScenarioSummary Scenario(IReadOnlyList<ScanSignal> signals, ScanScenario scenario, PositionDirection direction)
    {
        ScanTrade[] trades =
        [
            .. signals
                .SelectMany(signal => (signal.Trades ?? [])
                    .Where(trade => trade.Scenario == scenario && trade.Direction == direction)
                    .Select(trade => (signal.Symbol, Trade: trade)))
                .OrderBy(item => item.Trade.EnteredAt)
                .ThenBy(item => item.Symbol, StringComparer.Ordinal)
                .Select(item => item.Trade)
        ];

        if (trades.Length == 0)
        {
            return new ScanScenarioSummary(
                scenario,
                Trades: 0,
                WinningShare: null,
                MeanR: null,
                MedianR: null,
                TotalR: 0,
                MeanPercent: null,
                LongestLosingStreak: 0,
                MeanMinutes: null,
                direction);
        }

        decimal[] results = [.. trades.Select(trade => trade.ResultR).Order()];
        int middle = results.Length / 2;
        var streak = 0;
        var longest = 0;

        foreach (ScanTrade trade in trades)
        {
            streak = trade.ResultR < 0 ? streak + 1 : 0;
            longest = Math.Max(longest, streak);
        }

        return new ScanScenarioSummary(
            scenario,
            trades.Length,
            (decimal)trades.Count(trade => trade.ResultR > 0) / trades.Length,
            results.Sum() / results.Length,
            results.Length % 2 == 1 ? results[middle] : (results[middle - 1] + results[middle]) / 2m,
            results.Sum(),
            trades.Sum(trade => trade.ResultPercent) / trades.Length,
            longest,
            trades.Sum(trade => (decimal)(trade.ExitedAt - trade.EnteredAt).TotalMinutes) / trades.Length,
            direction);
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