using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Api.Bots.Converters;

internal static class BotTradingRecordResponseConverter
{
    public static BotFillPageResponse ToResponse(this BotFillPageInternal page)
    {
        return new BotFillPageResponse(
            [
                .. page.Items.Select(fill => new BotFillResponse(
                    fill.Sequence,
                    fill.Id,
                    fill.EngineOrderId,
                    fill.Side,
                    fill.Tag,
                    ExactGraphNumberParser.Format(fill.GrossBase),
                    ExactGraphNumberParser.Format(fill.GrossQuote),
                    ExactGraphNumberParser.Format(fill.Fee),
                    fill.FeeAsset,
                    ExactGraphNumberParser.Format(fill.Price),
                    fill.FilledAt,
                    fill.CommissionPending))
            ],
            page.Next);
    }

    public static BotFeedPageResponse ToResponse(this BotFeedPageInternal page)
    {
        return new BotFeedPageResponse(
            [.. page.Items.Select(entry => new BotFeedEntryResponse(entry.Sequence, entry.Id, entry.Kind, entry.Details, entry.OccurredAt))],
            page.Next);
    }

    public static BotPositionResponse ToResponse(this BotPositionInternal position)
    {
        return new BotPositionResponse(
            ExactGraphNumberParser.Format(position.BaseTotal),
            ExactGraphNumberParser.Format(position.QuoteTotal),
            ExactGraphNumberParser.Format(position.CostBasis),
            ExactGraphNumberParser.Format(position.EntryFees),
            position.AverageCost is { } average ? ExactGraphNumberParser.Format(average) : null,
            position.AverageBuyPrice is { } buyPrice ? ExactGraphNumberParser.Format(buyPrice) : null,
            ExactGraphNumberParser.Format(position.RealizedPnl),
            position.OpenedAt,
            position.MarkPrice is { } mark ? ExactGraphNumberParser.Format(mark) : null,
            position.MarkPriceObservedAt,
            position.MarkPriceSource,
            position.UnrealizedPnl is { } unrealized ? ExactGraphNumberParser.Format(unrealized) : null,
            position.PositionFees is { } fees ? ExactGraphNumberParser.Format(fees) : null,
            [
                .. position.Cycles.Select(cycle => new BotPositionCycleResponse(
                    cycle.Id,
                    cycle.OpenedAt,
                    cycle.ClosedAt,
                    cycle.RealizedPnl is { } pnl ? ExactGraphNumberParser.Format(pnl) : null,
                    ExactGraphNumberParser.Format(cycle.Fees)))
            ]);
    }
}