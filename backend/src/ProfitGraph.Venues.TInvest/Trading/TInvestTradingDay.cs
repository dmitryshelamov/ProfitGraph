namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     A broker trading date with its exchange intervals. A day with a token outside the vocabulary is not known and plans
///     nothing.
/// </summary>
internal sealed record TInvestTradingDay(DateOnly Date, IReadOnlyList<TInvestSessionInterval> Sessions, bool Known)
{
    /// <summary>
    ///     The close of the day's last exchange interval, closing auctions included.
    /// </summary>
    public DateTimeOffset? End => Sessions.Count == 0 ? null : Sessions.Max(session => session.To);
}