using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Follows a card after a signal the way its bot trades it (ADR 0071, section 2): the limit entry rests at the card's price for
///     the card's wait and fills at that price when a minute trades through it; the limit target rests from the next minute and fills
///     at its price when a minute trades through it; the stop acts on a minute's close beyond it and leaves at that close by market.
///     A minute that reaches the target counts as the target, since the resting order fills before the minute closes. A trade still
///     open <see cref="Horizon" /> after its entry leaves at that minute's close by market. Results are in R of the card's sizing
///     (ADR 0065): the quote a trade makes or loses over what a stop exactly at its price loses with the costs. Deterministic.
/// </summary>
public static class ScanCardFollow
{
    /// <summary>
    ///     The longest a followed trade is held.
    /// </summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromHours(72);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The outcome of <paramref name="idea" /> proposed at the end of the minute at <paramref name="index" />; null when the minutes
    ///     end before its entry's wait or its trade does.
    /// </summary>
    public static ScanCardOutcome? Follow(IReadOnlyList<ScanCandle> minutes, int index, ScanTradeIdea idea, ScanTradeCosts costs)
    {
        ArgumentNullException.ThrowIfNull(minutes);
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentNullException.ThrowIfNull(costs);

        bool @short = idea.Direction == PositionDirection.Short;
        var wait = (int)(idea.EntryValidity / Minute);

        for (int i = index + 1; i <= index + wait; i++)
        {
            if (i >= minutes.Count)
            {
                return null;
            }

            if (@short ? minutes[i].High > idea.Entry : minutes[i].Low < idea.Entry)
            {
                return Trade(minutes, i, idea, costs, @short);
            }
        }

        return ScanCardOutcome.Unfilled;
    }

    /// <summary>
    ///     The result in R of leaving at <paramref name="exit" />: the move after the costs of that exit over what the stop risks.
    /// </summary>
    public static decimal ResultR(ScanTradeIdea idea, decimal exit, ScanTradeExit reason, ScanTradeCosts costs)
    {
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentNullException.ThrowIfNull(costs);

        decimal moved = (idea.Direction == PositionDirection.Short ? idea.Entry - exit : exit - idea.Entry) / idea.Entry;
        decimal paid = reason == ScanTradeExit.Target ? costs.AtTarget : costs.AtStop;

        return (moved - paid) / (Math.Abs(idea.Entry - idea.Stop) / idea.Entry + costs.AtStop);
    }

    private static ScanCardOutcome? Trade(IReadOnlyList<ScanCandle> minutes, int filled, ScanTradeIdea idea, ScanTradeCosts costs, bool @short)
    {
        DateTimeOffset entered = minutes[filled].OpenTime;
        var held = (int)(Horizon / Minute);

        for (int i = filled; i < filled + held; i++)
        {
            if (i >= minutes.Count)
            {
                return null;
            }

            ScanCandle minute = minutes[i];

            if (i > filled && (@short ? minute.Low < idea.Target : minute.High > idea.Target))
            {
                return Outcome(idea, entered, minute, idea.Target, ScanTradeExit.Target, costs);
            }

            if (@short ? minute.Close > idea.Stop : minute.Close < idea.Stop)
            {
                return Outcome(idea, entered, minute, minute.Close, ScanTradeExit.Stop, costs);
            }
        }

        ScanCandle last = minutes[filled + held - 1];

        return Outcome(idea, entered, last, last.Close, ScanTradeExit.Time, costs);
    }

    private static ScanCardOutcome Outcome(ScanTradeIdea idea, DateTimeOffset entered, ScanCandle minute, decimal exit, ScanTradeExit reason, ScanTradeCosts costs)
    {
        return new ScanCardOutcome(Filled: true, reason, ResultR(idea, exit, reason, costs), entered, minute.OpenTime + Minute);
    }
}