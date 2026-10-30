namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     How much an auto trade rule may trade (ADR 0071, section 4): its trades open at once, its trades created in a UTC day and the
///     quote its trades closed in a UTC day may lose before it pauses until the next day.
/// </summary>
public sealed record ScanAutoTradeLimits
{
    public const int DefaultMaxOpenTrades = 3;
    public const int DefaultMaxTradesPerDay = 10;

    /// <summary>
    ///     The default daily loss limit in the settings' risks.
    /// </summary>
    public const decimal DefaultLossRisks = 3m;

    public int MaxOpenTrades { get; }
    public int MaxTradesPerDay { get; }
    public decimal DailyLossLimit { get; }

    public ScanAutoTradeLimits(int maxOpenTrades, int maxTradesPerDay, decimal dailyLossLimit)
    {
        if (maxOpenTrades is < 1 or > 100 || maxTradesPerDay is < 1 or > 1000 || dailyLossLimit <= 0m)
        {
            throw new ScanAutoTradeRuleException(
                ScanAutoTradeRuleException.InvalidRule,
                "An auto trade rule opens at least one trade at once and a day and may lose a positive amount a day.");
        }

        MaxOpenTrades = maxOpenTrades;
        MaxTradesPerDay = maxTradesPerDay;
        DailyLossLimit = dailyLossLimit;
    }

    /// <summary>
    ///     Why a new trade may not be created, or null when it may.
    /// </summary>
    public ScanAutoTradeSkip? Admits(int openTrades, int todayTrades, decimal todayLoss)
    {
        return openTrades >= MaxOpenTrades ? ScanAutoTradeSkip.LimitOpen
            : todayTrades >= MaxTradesPerDay ? ScanAutoTradeSkip.LimitDay
            : todayLoss >= DailyLossLimit ? ScanAutoTradeSkip.Paused
            : null;
    }
}