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

        ScanCardProgress progress = ScanCardProgress.Start;

        for (int i = index + 1; i < minutes.Count && !progress.Done; i++)
        {
            progress = Next(progress, idea, costs, minutes[i]);
        }

        return progress.Outcome;
    }

    /// <summary>
    ///     The card after one more closed <paramref name="minute" />: a paper trade (section 7) and the backtest step through the same
    ///     rules. A card with an outcome stays as it is.
    /// </summary>
    public static ScanCardProgress Next(ScanCardProgress progress, ScanTradeIdea idea, ScanTradeCosts costs, ScanCandle minute)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(minute);

        if (progress.Done)
        {
            return progress;
        }

        bool @short = idea.Direction == PositionDirection.Short;

        if (progress.EnteredAt is null)
        {
            int waited = progress.Waited + 1;

            if (@short ? minute.High > idea.Entry : minute.Low < idea.Entry)
            {
                return Held(new ScanCardProgress(waited, minute.OpenTime, Held: 1, Outcome: null), idea, costs, minute, filling: true);
            }

            return waited >= (int)(idea.EntryValidity / Minute)
                ? progress with { Waited = waited, Outcome = ScanCardOutcome.Unfilled }
                : progress with { Waited = waited };
        }

        return Held(progress with { Held = progress.Held + 1 }, idea, costs, minute, filling: false);
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

    private static ScanCardProgress Held(ScanCardProgress progress, ScanTradeIdea idea, ScanTradeCosts costs, ScanCandle minute, bool filling)
    {
        bool @short = idea.Direction == PositionDirection.Short;
        DateTimeOffset entered = progress.EnteredAt!.Value;

        if (!filling && (@short ? minute.Low < idea.Target : minute.High > idea.Target))
        {
            return progress with { Outcome = Outcome(idea, entered, minute, idea.Target, ScanTradeExit.Target, costs) };
        }

        if (@short ? minute.Close > idea.Stop : minute.Close < idea.Stop)
        {
            return progress with { Outcome = Outcome(idea, entered, minute, minute.Close, ScanTradeExit.Stop, costs) };
        }

        return progress.Held >= (int)(Horizon / Minute) ? progress with { Outcome = Outcome(idea, entered, minute, minute.Close, ScanTradeExit.Time, costs) } : progress;
    }

    private static ScanCardOutcome Outcome(ScanTradeIdea idea, DateTimeOffset entered, ScanCandle minute, decimal exit, ScanTradeExit reason, ScanTradeCosts costs)
    {
        return new ScanCardOutcome(Filled: true, reason, ResultR(idea, exit, reason, costs), entered, minute.OpenTime + Minute);
    }
}