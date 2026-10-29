using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The trades a scanner trade card proposes for a pair now (ADR 0060), by the rules the preset backtest simulates (ADR 0059,
///     section 8), with the current price in place of the next minute's open: <see cref="ScanScenario.Levels" /> from the support to the
///     resistance and <see cref="ScanScenario.Target2R" /> from the support to two risks above, both entering at the price within an
///     hour; <see cref="ScanScenario.Pullback" /> a limit halfway down the last hour's rise, stopped at that hour's lowest low, targeting
///     two risks above and waiting four hours. Where shorts are allowed (ADR 0061) the mirrors join them: <c>Levels</c> from the
///     resistance down to the support, <c>Target2R</c> from the resistance to two risks below, and <c>Pullback</c> a limit halfway up
///     the last hour's fall, stopped at its highest high. Deterministic for identical inputs.
/// </summary>
public static class ScanTradeIdeas
{
    private const decimal TargetRisks = 2m;

    /// <summary>
    ///     How long the pullback's limit waits, in the backtest and on a card.
    /// </summary>
    public static readonly TimeSpan PullbackWait = TimeSpan.FromHours(4);

    /// <summary>
    ///     How long an entry at the current price waits on a card before it is cancelled.
    /// </summary>
    public static readonly TimeSpan MarketEntryWait = TimeSpan.FromHours(1);

    public static IReadOnlyList<ScanTradeIdea> Of(
        decimal price,
        decimal? support,
        decimal? resistance,
        decimal? lastHourLow,
        decimal? lastHourHigh = null,
        bool shorts = false)
    {
        List<ScanTradeIdea> ideas = [];

        if (price <= 0m)
        {
            return ideas;
        }

        if (support is { } stop && stop > 0m && stop < price)
        {
            if (resistance is { } target && target > price)
            {
                ideas.Add(Idea(ScanScenario.Levels, price, stop, target, MarketEntryWait));
            }

            ideas.Add(Idea(ScanScenario.Target2R, price, stop, price + TargetRisks * (price - stop), MarketEntryWait));
        }

        if (lastHourLow is { } low && low > 0m && low < price)
        {
            decimal limit = PullbackLimit(price, low);
            ideas.Add(Idea(ScanScenario.Pullback, limit, low, limit + TargetRisks * (limit - low), PullbackWait));
        }

        if (shorts)
        {
            ideas.AddRange(Shorts(price, support, resistance, lastHourHigh));
        }

        return ideas;
    }

    /// <summary>
    ///     The quote to spend so that a stop at <paramref name="stop" /> loses <paramref name="risk" /> after a fee of
    ///     <paramref name="feeRate" /> on the entry and on the exit.
    /// </summary>
    public static decimal Amount(decimal risk, decimal entry, decimal stop, decimal feeRate)
    {
        if (risk <= 0m || stop <= 0m || entry <= 0m || stop == entry || feeRate < 0m)
        {
            throw new ArgumentException("A trade risks a positive amount with its stop away from its entry.");
        }

        return risk / (Math.Abs(entry - stop) / entry + 2 * feeRate);
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

    private static IEnumerable<ScanTradeIdea> Shorts(decimal price, decimal? support, decimal? resistance, decimal? lastHourHigh)
    {
        if (resistance is { } stop && stop > price)
        {
            if (support is { } target && target > 0m && target < price)
            {
                yield return Idea(ScanScenario.Levels, price, stop, target, MarketEntryWait, PositionDirection.Short);
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