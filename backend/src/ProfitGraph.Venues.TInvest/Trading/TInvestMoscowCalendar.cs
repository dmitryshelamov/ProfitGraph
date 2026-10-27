namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The calendar fallback for the end of a MOEX trading day (ADR 0032). MOEX trading days follow the calendar days of Moscow
///     time (UTC+3, no daylight saving since 2014), evening and weekend sessions included, so a day cannot outlast the midnight
///     that closes its Moscow date. It is used only when <c>TradingSchedules</c> cannot answer for a past day, and only for the
///     exchange codes listed here.
/// </summary>
internal static class TInvestMoscowCalendar
{
    /// <summary>
    ///     The <c>Share.Exchange</c> values that T-Invest returns for MOEX shares and that this calendar is proven for: the code
    ///     of every TQBR share seen (ADR 0023). An exchange not listed is not guessed.
    /// </summary>
    private static readonly string[] Exchanges = ["moex_mrng_evng_e_wknd_dlr"];

    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    public static bool Covers(string exchange)
    {
        return Exchanges.Contains(exchange, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The first midnight in Moscow after the instant, which no trading day holding the instant outlasts.
    /// </summary>
    public static DateTimeOffset DayEnd(DateTimeOffset instant)
    {
        DateTime moscow = instant.ToOffset(MoscowOffset).DateTime;

        return new DateTimeOffset(moscow.Date.AddDays(1), MoscowOffset);
    }
}
