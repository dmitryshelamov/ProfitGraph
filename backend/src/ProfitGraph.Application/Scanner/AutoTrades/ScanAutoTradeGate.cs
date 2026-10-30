using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.AutoTrades;

/// <summary>
///     Whether an auto trade request may trade now (ADR 0071, section 6): its snapshot is still fresh, its rule is on and not paused,
///     under its limits, and without an open trade on the pair. A rule whose closed trades of the day lost its limit pauses here.
///     Checked when the Worker claims the request and again when it takes the card.
/// </summary>
internal static class ScanAutoTradeGate
{
    public static ScanAutoTradeSkip? Check(
        ScanAutoTradeRequest request,
        ScanAutoTradeRule? rule,
        IReadOnlyList<ScanPaperTrade> recent,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        DateTimeOffset dayStart = ScanAutoTradeTally.DayStart(now);
        DateOnly day = DateOnly.FromDateTime(dayStart.UtcDateTime);

        if (now - request.SnapshotAt > lifetime)
        {
            return ScanAutoTradeSkip.Expired;
        }

        if (rule is null || !rule.TradesOn(day))
        {
            return ScanAutoTradeSkip.Paused;
        }

        ScanAutoTradeTally tally = ScanAutoTradeTally.Of(recent, dayStart);

        if (rule.Limits.Admits(tally.Open, tally.Today, tally.Loss) is { } skip)
        {
            if (skip == ScanAutoTradeSkip.Paused)
            {
                rule.Pause(day, now);
            }

            return skip;
        }

        return recent.Any(trade => trade.IsOpen && trade.Symbol == request.Symbol) ? ScanAutoTradeSkip.PairBusy : null;
    }
}