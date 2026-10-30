using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Api.Scanner.Converters;

internal static class ScanTradeCardResponseConverter
{
    public static ScanTradeIdeasResponse ToResponse(this ScanTradeIdeasInternal ideas)
    {
        return new ScanTradeIdeasResponse(
            ideas.Venue,
            ideas.Symbol,
            ScanSnapshotResponseConverter.Text(ideas.Price),
            ideas.HasSettings,
            [
                .. ideas.Ideas.Select(idea => new ScanTradeIdeaResponse(
                    idea.Timeframe,
                    idea.Scenario.ToString(),
                    ExactGraphNumberParser.Format(idea.Entry),
                    ExactGraphNumberParser.Format(idea.Stop),
                    ExactGraphNumberParser.Format(idea.Target),
                    ExactGraphNumberParser.Format(idea.RewardRisk),
                    (int)idea.EntryValidity.TotalMinutes,
                    ScanSnapshotResponseConverter.Text(idea.Amount),
                    ScanSnapshotResponseConverter.Text(idea.Risk),
                    idea.Taken,
                    idea.Direction.ToString(),
                    ScanSnapshotResponseConverter.Text(idea.Profit)))
            ],
            new ScanTradeCostsResponse(
                ExactGraphNumberParser.Format(ideas.Costs.Costs.LimitFeeRate),
                ExactGraphNumberParser.Format(ideas.Costs.Costs.MarketFeeRate),
                ExactGraphNumberParser.Format(ideas.Costs.Costs.Spread),
                ideas.Costs.FeesFromVenue,
                ideas.Costs.SpreadKnown),
            ideas.TightStops);
    }

    public static ScanTradeSettingsResponse ToResponse(this ScanTradeSettingsInternal settings)
    {
        return new ScanTradeSettingsResponse(
            settings.Venue,
            settings.CredentialId,
            ExactGraphNumberParser.Format(settings.Risk),
            ExactGraphNumberParser.Format(settings.MaxAmount),
            ScanSnapshotResponseConverter.Text(settings.Leverage));
    }

    public static ScanLiveTradeResponse ToResponse(this ScanTradeInternal trade)
    {
        return new ScanLiveTradeResponse(
            trade.Id,
            trade.Venue,
            trade.Symbol,
            trade.Scenario.ToString(),
            trade.LevelsTimeframe,
            ExactGraphNumberParser.Format(trade.Entry),
            ExactGraphNumberParser.Format(trade.Stop),
            ExactGraphNumberParser.Format(trade.Target),
            ExactGraphNumberParser.Format(trade.Amount),
            ExactGraphNumberParser.Format(trade.Risk),
            trade.BotId,
            trade.CreatedAt,
            trade.ExpiresAt,
            trade.Status.ToString(),
            trade.OpenedAt,
            trade.ClosedAt,
            ScanSnapshotResponseConverter.Text(trade.RealizedPnl),
            trade.FailureCode,
            trade.Direction.ToString());
    }
}