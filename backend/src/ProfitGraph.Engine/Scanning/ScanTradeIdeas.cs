using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The trades a scanner trade card proposes for a pair now (ADR 0060), by the rules the preset backtest simulates (ADR 0059,
///     section 8), with the current price in place of the next minute's open: <see cref="ScanScenario.Levels" /> from the support to the
///     resistance and <see cref="ScanScenario.Target2R" /> from the support to two risks above, both entering at the price within an
///     hour; <see cref="ScanScenario.Pullback" /> a limit halfway down the last hour's rise, stopped at that hour's lowest low, targeting
///     two risks above and waiting four hours. Where shorts are allowed (ADR 0061) the mirrors join them: <c>Levels</c> from the
///     resistance down to the support, <c>Target2R</c> from the resistance to two risks below, and <c>Pullback</c> a limit halfway up
///     the last hour's fall, stopped at its highest high. <see cref="ScanScenario.Extreme" /> enters at the price with its stop beyond the
///     extreme of the last four hours, the lowest low for a long and the highest high for a short, and targets two risks away (ADR 0072):
///     a short after a pump puts its stop above the pump's peak rather than at the nearest resistance. With the average range of the
///     levels' timeframe (ADR 0070) a stop at a level lies a quarter of that range beyond it, and an entry at the price whose stop is
///     nearer than one range is not proposed: a candle of the timeframe would reach it by its usual move. Deterministic for identical
///     inputs.
/// </summary>
public static class ScanTradeIdeas
{
    private const decimal TargetRisks = 2m;

    /// <summary>
    ///     How far beyond its level a stop lies, in average ranges of the levels' timeframe (ADR 0070).
    /// </summary>
    public const decimal StopBuffer = 0.25m;

    /// <summary>
    ///     The nearest a stop of an entry at the price may be, in average ranges of the levels' timeframe (ADR 0070).
    /// </summary>
    public const decimal MinimumStopRanges = 1m;

    /// <summary>
    ///     The significant digits a price taken from a level keeps (ADR 0070): a level is a mean of prices with any number of digits,
    ///     and a trade's prices are stored with 18 decimals.
    /// </summary>
    private const int StopDigits = 8;

    /// <summary>
    ///     How long the pullback's limit waits, in the backtest and on a card.
    /// </summary>
    public static readonly TimeSpan PullbackWait = TimeSpan.FromHours(4);

    /// <summary>
    ///     How long an entry at the current price waits on a card before it is cancelled.
    /// </summary>
    public static readonly TimeSpan MarketEntryWait = TimeSpan.FromHours(1);

    /// <summary>
    ///     The window whose lowest low or highest high guards an <see cref="ScanScenario.Extreme" /> trade (ADR 0072).
    /// </summary>
    public static readonly TimeSpan ExtremeWindow = TimeSpan.FromHours(4);

    public static IReadOnlyList<ScanTradeIdea> Of(
        decimal price,
        decimal? support,
        decimal? resistance,
        decimal? lastHourLow,
        decimal? lastHourHigh = null,
        bool shorts = false,
        decimal? averageRange = null,
        decimal? extremeLow = null,
        decimal? extremeHigh = null)
    {
        List<ScanTradeIdea> ideas = [];

        if (price <= 0m)
        {
            return ideas;
        }

        if (support is { } level && level > 0m && level < price && LevelStop(price, level, averageRange) is { } stop)
        {
            if (resistance is { } target && target > price)
            {
                ideas.Add(Idea(ScanScenario.Levels, price, stop, LevelTarget(price, target), MarketEntryWait));
            }

            ideas.Add(Idea(ScanScenario.Target2R, price, stop, price + TargetRisks * (price - stop), MarketEntryWait));
        }

        if (lastHourLow is { } low && low > 0m && low < price)
        {
            decimal limit = PullbackLimit(price, low);
            ideas.Add(Idea(ScanScenario.Pullback, limit, low, limit + TargetRisks * (limit - low), PullbackWait));
        }

        if (extremeLow is { } bottom && bottom > 0m && bottom < price && LevelStop(price, bottom, averageRange) is { } belowBottom)
        {
            ideas.Add(Idea(ScanScenario.Extreme, price, belowBottom, price + TargetRisks * (price - belowBottom), MarketEntryWait));
        }

        if (shorts)
        {
            ideas.AddRange(Shorts(price, support, resistance, lastHourHigh, averageRange, extremeHigh));
        }

        return ideas;
    }

    /// <summary>
    ///     The quote to spend so that a stop at <paramref name="stop" /> loses <paramref name="risk" /> with the trade's
    ///     <paramref name="costs" /> (ADR 0065).
    /// </summary>
    public static decimal Amount(decimal risk, decimal entry, decimal stop, ScanTradeCosts costs)
    {
        ArgumentNullException.ThrowIfNull(costs);

        if (risk <= 0m || stop <= 0m || entry <= 0m || stop == entry)
        {
            throw new ArgumentException("A trade risks a positive amount with its stop away from its entry.");
        }

        return risk / (Math.Abs(entry - stop) / entry + costs.AtStop);
    }

    /// <summary>
    ///     The stop of an entry at <paramref name="entry" /> guarded by <paramref name="level" /> (ADR 0070): the level moved a
    ///     <see cref="StopBuffer" /> of the average range beyond it, or null when that stop is nearer the entry than
    ///     <see cref="MinimumStopRanges" /> ranges or not above zero. Without an average range the stop is the level. The stop keeps
    ///     <see cref="StopDigits" /> significant digits, rounded away from the entry.
    /// </summary>
    public static decimal? LevelStop(decimal entry, decimal level, decimal? averageRange)
    {
        bool below = level < entry;

        if (averageRange is not { } range || range <= 0m)
        {
            return Significant(level, below);
        }

        decimal stop = below ? level - StopBuffer * range : level + StopBuffer * range;

        if (stop <= 0m)
        {
            return null;
        }

        stop = Significant(stop, below);

        return Math.Abs(entry - stop) < MinimumStopRanges * range ? null : stop;
    }

    /// <summary>
    ///     The target at <paramref name="level" />, with <see cref="StopDigits" /> significant digits rounded towards the entry.
    /// </summary>
    public static decimal LevelTarget(decimal entry, decimal level)
    {
        return Significant(level, level > entry);
    }

    /// <summary>
    ///     <paramref name="value" /> with <see cref="StopDigits" /> significant digits, rounded down or up.
    /// </summary>
    private static decimal Significant(decimal value, bool down)
    {
        int places = Math.Clamp(StopDigits - 1 - (int)Math.Floor(Math.Log10((double)value)), min: 0, max: 20);
        var scale = 1m;

        for (var i = 0; i < places; i++)
        {
            scale *= 10m;
        }

        return (down ? Math.Floor(value * scale) : Math.Ceiling(value * scale)) / scale;
    }

    /// <summary>
    ///     The pullback's limit: halfway from the close down to the last hour's lowest low.
    /// </summary>
    internal static decimal PullbackLimit(decimal close, decimal low)
    {
        return close - (close - low) / 2;
    }

    /// <summary>
    ///     A short pullback's limit: halfway from the close up to the last hour's highest high (ADR 0061).
    /// </summary>
    internal static decimal RallyLimit(decimal close, decimal high)
    {
        return close + (high - close) / 2;
    }

    private static IEnumerable<ScanTradeIdea> Shorts(
        decimal price,
        decimal? support,
        decimal? resistance,
        decimal? lastHourHigh,
        decimal? averageRange,
        decimal? extremeHigh)
    {
        if (resistance is { } level && level > price && LevelStop(price, level, averageRange) is { } stop)
        {
            if (support is { } target && target > 0m && target < price)
            {
                yield return Idea(ScanScenario.Levels, price, stop, LevelTarget(price, target), MarketEntryWait, PositionDirection.Short);
            }

            decimal twoRisks = price - TargetRisks * (stop - price);

            if (twoRisks > 0m)
            {
                yield return Idea(ScanScenario.Target2R, price, stop, twoRisks, MarketEntryWait, PositionDirection.Short);
            }
        }

        if (lastHourHigh is { } high && high > price)
        {
            decimal limit = RallyLimit(price, high);
            decimal target = limit - TargetRisks * (high - limit);

            if (target > 0m)
            {
                yield return Idea(ScanScenario.Pullback, limit, high, target, PullbackWait, PositionDirection.Short);
            }
        }

        if (extremeHigh is { } peak && peak > price && LevelStop(price, peak, averageRange) is { } abovePeak)
        {
            decimal below = price - TargetRisks * (abovePeak - price);

            if (below > 0m)
            {
                yield return Idea(ScanScenario.Extreme, price, abovePeak, below, MarketEntryWait, PositionDirection.Short);
            }
        }
    }

    private static ScanTradeIdea Idea(
        ScanScenario scenario,
        decimal entry,
        decimal stop,
        decimal target,
        TimeSpan validity,
        PositionDirection direction = PositionDirection.Long)
    {
        return new ScanTradeIdea(scenario, entry, stop, target, (target - entry) / (entry - stop), validity, direction);
    }
}