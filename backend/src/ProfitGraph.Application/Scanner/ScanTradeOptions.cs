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
    ///     The fee on each side a card's amount is sized with; the preset backtest assumes the same.
    /// </summary>
    public decimal FeeRate { get; init; } = 0.001m;

    /// <summary>
    ///     How far, as a fraction of the current price, a created trade's entry may be from it.
    /// </summary>
    public decimal MaxEntryDeviation { get; init; } = 0.05m;

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
        return IsTimeframe(Timeframe) && FeeRate is >= 0m and < 0.1m && MaxEntryDeviation is > 0m and < 1m && TrackInterval > TimeSpan.Zero && TrackBatchSize > 0 &&
            ListLimit > 0;
    }
}