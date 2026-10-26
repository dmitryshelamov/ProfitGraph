using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.MarketData.Converters;
using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Api.Bots.Converters;

internal static class BotCandlesResponseConverter
{
    public static BotCandlesResponse ToResponse(this BotCandlesInternal page)
    {
        return new BotCandlesResponse(
            page.Venue,
            page.Symbol,
            page.Timeframe,
            page.From,
            page.To,
            page.LatestClosed,
            page.Status,
            page.ErrorCode,
            [.. page.Candles.Select(candle => candle.ToResponse())],
            [
                .. page.Fills.Select(fill => new BotChartFillResponse(
                    fill.Id,
                    fill.FilledAt,
                    fill.Side,
                    ExactGraphNumberParser.Format(fill.Price),
                    ExactGraphNumberParser.Format(fill.GrossBase),
                    fill.Tag))
            ],
            page.FillsTruncated);
    }
}