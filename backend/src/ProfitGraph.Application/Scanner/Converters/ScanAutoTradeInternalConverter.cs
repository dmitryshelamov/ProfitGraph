using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Converters;

internal static class ScanAutoTradeInternalConverter
{
    public static ScanAutoTradeRuleInternal ToInternal(this ScanAutoTradeRule rule)
    {
        return new ScanAutoTradeRuleInternal(
            rule.Id.Value,
            rule.PresetId.Value,
            rule.Venue.Value,
            rule.Card.Direction,
            rule.Card.Scenario,
            rule.Card.Timeframe,
            rule.Card.MinExpectedR,
            rule.Card.MinChance,
            rule.Limits.MaxOpenTrades,
            rule.Limits.MaxTradesPerDay,
            rule.Limits.DailyLossLimit,
            rule.Mode,
            rule.PausedOn,
            rule.Proof,
            rule.ModeSince,
            rule.UpdatedAt);
    }

    public static ScanAutoTradeRequestInternal ToInternal(this ScanAutoTradeRequest request)
    {
        return new ScanAutoTradeRequestInternal(
            request.Id.Value,
            request.Symbol,
            request.SnapshotAt,
            request.Price,
            request.Skip?.ToString() ?? request.Status.ToString(),
            request.ErrorCode,
            request.PaperTradeId?.Value,
            request.HandledAt);
    }

    public static ScanPaperTradeInternal ToInternal(this ScanPaperTrade trade)
    {
        ScanCardProgress progress = trade.Progress;
        string status = progress.Outcome switch
        {
            null => progress.EnteredAt is null ? "Waiting" : "Open",
            { Filled: false } => "Unfilled",
            { Exit: { } exit } => exit.ToString(),
            _ => "Open"
        };

        return new ScanPaperTradeInternal(
            trade.Id.Value,
            trade.Symbol,
            trade.Timeframe,
            trade.Idea.Scenario,
            trade.Idea.Direction,
            trade.Idea.Entry,
            trade.Idea.Stop,
            trade.Idea.Target,
            trade.Amount,
            trade.Risk,
            trade.ExpectedR,
            trade.CreatedAt,
            status,
            progress.Outcome?.EnteredAt ?? progress.EnteredAt,
            progress.Outcome?.ExitedAt,
            progress.Outcome?.ResultR,
            trade.Pnl);
    }
}