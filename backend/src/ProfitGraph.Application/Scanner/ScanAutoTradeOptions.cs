using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner;

/// <summary>
///     The scanner's auto trades (ADR 0071, sections 5–7), from the <c>ScanAutoTrades</c> section.
/// </summary>
public sealed class ScanAutoTradeOptions
{
    public const string SectionName = "ScanAutoTrades";

    /// <summary>
    ///     The fewest closed trades a rule's cards need in a backtest.
    /// </summary>
    public int MinBacktestTrades { get; init; } = 100;

    /// <summary>
    ///     The least mean R after costs a rule's cards need in each third of a backtest.
    /// </summary>
    public decimal MinBacktestMeanR { get; init; } = 0.05m;

    /// <summary>
    ///     The fewest closed paper trades before a rule trades live.
    /// </summary>
    public int MinPaperTrades { get; init; } = 30;

    /// <summary>
    ///     The oldest snapshot a request is still handled for.
    /// </summary>
    public TimeSpan RequestLifetime { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     How long handled requests are kept.
    /// </summary>
    public TimeSpan RequestRetention { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     How often the Worker handles pending requests.
    /// </summary>
    public TimeSpan HandleInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How many requests one round handles.
    /// </summary>
    public int HandleBatchSize { get; init; } = 50;

    /// <summary>
    ///     How many of the owner's latest backtests are searched for a rule's proof.
    /// </summary>
    public int EvidenceLimit { get; init; } = 20;

    /// <summary>
    ///     How many requests and paper trades the page lists.
    /// </summary>
    public int ListLimit { get; init; } = 50;

    public ScanAutoTradeThresholds Thresholds => new ScanAutoTradeThresholds(MinBacktestTrades, MinBacktestMeanR, MinPaperTrades);

    public bool IsValid()
    {
        return MinBacktestTrades > 0 && MinPaperTrades > 0 && RequestLifetime > TimeSpan.Zero && RequestRetention > TimeSpan.Zero &&
            HandleInterval > TimeSpan.Zero && HandleBatchSize > 0 && EvidenceLimit > 0 && ListLimit > 0;
    }
}