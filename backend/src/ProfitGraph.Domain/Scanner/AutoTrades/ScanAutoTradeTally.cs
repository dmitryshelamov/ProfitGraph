namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     What a rule's trades amount to on a UTC day (ADR 0071, section 4): those open or waiting for their entry, those created that day
///     and the quote those closed that day lost together, zero when they made money.
/// </summary>
public sealed record ScanAutoTradeTally(int Open, int Today, decimal Loss)
{
    public static ScanAutoTradeTally Of(IEnumerable<ScanPaperTrade> trades, DateTimeOffset dayStart)
    {
        ArgumentNullException.ThrowIfNull(trades);

        ScanPaperTrade[] all = [.. trades];
        decimal pnl = all.Where(trade => trade.Progress.Outcome?.ExitedAt >= dayStart).Sum(trade => trade.Pnl ?? 0m);

        return new ScanAutoTradeTally(all.Count(trade => trade.IsOpen), all.Count(trade => trade.CreatedAt >= dayStart), Math.Max(-pnl, val2: 0m));
    }

    /// <summary>
    ///     The start of the UTC day of <paramref name="now" />.
    /// </summary>
    public static DateTimeOffset DayStart(DateTimeOffset now)
    {
        return new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
    }
}