using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Scanner.Repositories.Converters;

/// <summary>
///     A paper trade's progress is stored in columns: <c>filled</c> is empty until the entry fills or its wait ends, and
///     <c>last_minute</c> keeps the next minute to step through.
/// </summary>
internal static class ScanPaperTradeDbConverter
{
    public static ScanPaperTrade ToDomain(this ScanPaperTradeDb row)
    {
        var idea = new ScanTradeIdea(
            Enum.Parse<ScanScenario>(row.Scenario),
            row.Entry,
            row.Stop,
            row.Target,
            row.RewardRisk,
            TimeSpan.FromMinutes(row.EntryWaitMinutes),
            Enum.Parse<PositionDirection>(row.Direction));
        ScanCardOutcome? outcome = row.Filled switch
        {
            null => null,
            false => ScanCardOutcome.Unfilled,
            true => new ScanCardOutcome(
                Filled: true,
                Enum.Parse<ScanTradeExit>(row.ExitReason!),
                row.ResultR,
                row.EnteredAt,
                row.ExitedAt)
        };

        return ScanPaperTrade.Restore(
            new ScanPaperTradeId(row.Id),
            new ScanAutoTradeRuleId(row.RuleId),
            new VenueId(row.Venue),
            row.Symbol,
            row.Timeframe,
            idea,
            row.Amount,
            row.Risk,
            new ScanTradeCosts(row.LimitFeeRate, row.MarketFeeRate, row.Spread),
            row.ExpectedR,
            row.Chance,
            row.CreatedAt,
            row.LastMinute,
            new ScanCardProgress(row.Waited, row.EnteredAt, row.Held, outcome));
    }

    public static object ToParameters(this ScanPaperTrade trade)
    {
        ScanCardProgress progress = trade.Progress;

        return new
        {
            Id = trade.Id.Value,
            RuleId = trade.RuleId.Value,
            Venue = trade.Venue.Value,
            trade.Symbol,
            trade.Timeframe,
            Scenario = trade.Idea.Scenario.ToString(),
            Direction = trade.Idea.Direction.ToString(),
            trade.Idea.Entry,
            trade.Idea.Stop,
            trade.Idea.Target,
            trade.Idea.RewardRisk,
            EntryWaitMinutes = (int)trade.Idea.EntryValidity.TotalMinutes,
            trade.Amount,
            trade.Risk,
            trade.Costs.LimitFeeRate,
            trade.Costs.MarketFeeRate,
            trade.Costs.Spread,
            trade.ExpectedR,
            trade.Chance,
            trade.CreatedAt,
            LastMinute = trade.NextMinute,
            progress.Waited,
            EnteredAt = progress.Outcome?.EnteredAt ?? progress.EnteredAt,
            progress.Held,
            ExitReason = progress.Outcome?.Exit?.ToString(),
            progress.Outcome?.Filled,
            progress.Outcome?.ResultR,
            progress.Outcome?.ExitedAt
        };
    }
}