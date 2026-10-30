using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     A card an auto trade rule took on paper (ADR 0071, section 7): the card's prices, the amount and risk of the owner's sizing, the
///     costs and expected R it was taken with, and where it stands. The scanner steps it through each closed minute of its pair after
///     the snapshot that signalled it, by the rules the preset backtest follows cards with, so a rule's paper results compare with its
///     backtest's.
/// </summary>
public sealed class ScanPaperTrade : AggregateRoot
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public ScanPaperTradeId Id { get; }
    public ScanAutoTradeRuleId RuleId { get; }
    public VenueId Venue { get; }
    public string Symbol { get; }
    public string Timeframe { get; }
    public ScanTradeIdea Idea { get; }
    public decimal Amount { get; }
    public decimal Risk { get; }
    public ScanTradeCosts Costs { get; }
    public decimal ExpectedR { get; }
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    ///     The open time of the next minute the trade steps through.
    /// </summary>
    public DateTimeOffset NextMinute { get; private set; }

    public ScanCardProgress Progress { get; private set; }

    public bool IsOpen => !Progress.Done;

    /// <summary>
    ///     The quote the closed trade made or lost: its result in R times the risk; null while it is open or when its entry did not fill.
    /// </summary>
    public decimal? Pnl => Progress.Outcome?.ResultR * Risk;

    private ScanPaperTrade(
        ScanPaperTradeId id,
        ScanAutoTradeRuleId ruleId,
        VenueId venue,
        string symbol,
        string timeframe,
        ScanTradeIdea idea,
        decimal amount,
        decimal risk,
        ScanTradeCosts costs,
        decimal expectedR,
        DateTimeOffset createdAt,
        DateTimeOffset nextMinute,
        ScanCardProgress progress)
    {
        if (amount <= 0m || risk <= 0m)
        {
            throw new ScanAutoTradeRuleException(ScanAutoTradeRuleException.InvalidRule, "A paper trade spends and risks a positive amount.");
        }

        Id = id;
        RuleId = ruleId;
        Venue = venue;
        Symbol = symbol;
        Timeframe = timeframe;
        Idea = idea ?? throw new ArgumentNullException(nameof(idea));
        Amount = amount;
        Risk = risk;
        Costs = costs ?? throw new ArgumentNullException(nameof(costs));
        ExpectedR = expectedR;
        CreatedAt = createdAt;
        NextMinute = nextMinute;
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    /// <summary>
    ///     A paper trade of a card proposed by the snapshot computed at <paramref name="snapshotAt" />: it steps from the minute after the
    ///     snapshot's last closed one.
    /// </summary>
    public static ScanPaperTrade Open(
        ScanAutoTradeRuleId ruleId,
        VenueId venue,
        string symbol,
        string timeframe,
        ScanTradeIdea idea,
        decimal amount,
        decimal risk,
        ScanTradeCosts costs,
        decimal expectedR,
        DateTimeOffset snapshotAt,
        DateTimeOffset now)
    {
        var nextMinute = new DateTimeOffset(snapshotAt.UtcTicks - snapshotAt.UtcTicks % Minute.Ticks, TimeSpan.Zero);

        return new ScanPaperTrade(
            ScanPaperTradeId.New(),
            ruleId,
            venue,
            symbol,
            timeframe,
            idea,
            amount,
            risk,
            costs,
            expectedR,
            now,
            nextMinute,
            ScanCardProgress.Start);
    }

    public static ScanPaperTrade Restore(
        ScanPaperTradeId id,
        ScanAutoTradeRuleId ruleId,
        VenueId venue,
        string symbol,
        string timeframe,
        ScanTradeIdea idea,
        decimal amount,
        decimal risk,
        ScanTradeCosts costs,
        decimal expectedR,
        DateTimeOffset createdAt,
        DateTimeOffset nextMinute,
        ScanCardProgress progress)
    {
        return new ScanPaperTrade(id, ruleId, venue, symbol, timeframe, idea, amount, risk, costs, expectedR, createdAt, nextMinute, progress);
    }

    /// <summary>
    ///     Steps through the closed minutes of the pair it has not seen, oldest first; answers whether the trade closed or expired now.
    /// </summary>
    public bool Advance(IEnumerable<ScanCandle> minutes)
    {
        ArgumentNullException.ThrowIfNull(minutes);

        bool wasOpen = IsOpen;

        foreach (ScanCandle minute in minutes.Where(minute => minute.OpenTime >= NextMinute).OrderBy(minute => minute.OpenTime))
        {
            if (!IsOpen)
            {
                break;
            }

            Progress = ScanCardFollow.Next(Progress, Idea, Costs, minute);
            NextMinute = minute.OpenTime + Minute;
        }

        return wasOpen && !IsOpen;
    }
}