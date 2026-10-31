namespace ProfitGraph.Application.Scanner;

/// <summary>
///     The scanner trade cards' settings (ADR 0060), from the <c>ScanTrades</c> section.
/// </summary>
public sealed class ScanTradeOptions
{
    public const string SectionName = "ScanTrades";

    /// <summary>
    ///     The candles a trade's bot runs on, whose closes check its stop; the cards' last hour is read in them too.
    /// </summary>
    public string Timeframe { get; init; } = "1m";

    /// <summary>
    ///     The timeframes whose levels give the cards' proposals, shortest first; those the venue does not offer are skipped. Minutes'
    ///     levels lie within a candle's noise and days' targets beyond a trade's day (ADR 0070), so the cards read 15 minutes to 4 hours.
    /// </summary>
    public string[] LevelTimeframes { get; init; } = ["15m", "1h", "4h"];

    /// <summary>
    ///     The fee on every order a card assumes when the venue's fee rates for the owner's credential cannot be read (ADR 0065); the
    ///     preset backtest assumes the same.
    /// </summary>
    public decimal FeeRate { get; init; } = 0.001m;

    /// <summary>
    ///     The fewest closed trades of the source backtests' cards of a card's kind (its timeframe, scenario and direction) that give it a
    ///     chance and an expected result (ADR 0071, section 1; ADR 0073).
    /// </summary>
    public int MinOddsTrades { get; init; } = 100;

    /// <summary>
    ///     How many imagined trades of what the level above predicts a level of a card's odds is shrunk towards (ADR 0073).
    /// </summary>
    public decimal OddsPriorTrades { get; init; } = 50m;

    /// <summary>
    ///     The most backtests a card's odds are pooled from (ADR 0073).
    /// </summary>
    public int OddsSources { get; init; } = 6;

    /// <summary>
    ///     How long after its period's end a backtest's counts weigh half as much in a card's odds (ADR 0073).
    /// </summary>
    public TimeSpan OddsHalfLife { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     How long the venue's fee rates of a credential and a pair are reused before the cards read them again.
    /// </summary>
    public TimeSpan FeeRatesLifetime { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     How far, as a fraction of the current price, a created trade's entry may be from it.
    /// </summary>
    public decimal MaxEntryDeviation { get; init; } = 0.05m;

    /// <summary>
    ///     The oldest price a trade's entry is checked against before it is sent (ADR 0067); without a newer one the entry is refused.
    /// </summary>
    public TimeSpan EntryPriceMaxAge { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>
    ///     How often the Worker cancels overdue entries and stops the bots of finished trades.
    /// </summary>
    public TimeSpan TrackInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     How many trades one tracking round looks at in each list.
    /// </summary>
    public int TrackBatchSize { get; init; } = 200;

    /// <summary>
    ///     How many trades a list of the owner's trades returns.
    /// </summary>
    public int ListLimit { get; init; } = 50;

    private static bool IsTimeframe(string key)
    {
        try
        {
            Engine.Market.Timeframe.Parse(key);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public bool IsValid()
    {
        return IsTimeframe(Timeframe) && LevelTimeframes.Length > 0 && LevelTimeframes.All(IsTimeframe) && FeeRate is >= 0m and < 0.1m &&
            FeeRatesLifetime > TimeSpan.Zero && MinOddsTrades > 0 && OddsPriorTrades > 0m && OddsSources > 0 && OddsHalfLife > TimeSpan.Zero &&
            MaxEntryDeviation is > 0m and < 1m && EntryPriceMaxAge > TimeSpan.Zero && TrackInterval > TimeSpan.Zero && TrackBatchSize > 0 &&
            ListLimit > 0;
    }
}