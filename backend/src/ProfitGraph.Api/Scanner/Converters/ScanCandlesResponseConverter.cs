using ProfitGraph.Api.MarketData.Converters;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Api.Scanner.Converters;

internal static class ScanCandlesResponseConverter
{
    public static ScanCandlesResponse ToResponse(this ScanCandlesInternal page)
    {
        return new ScanCandlesResponse(
            page.Venue,
            page.Symbol,
            page.Timeframe,
            page.From,
            page.To,
            page.LatestClosed,
            page.Status,
            page.ErrorCode,
            [.. page.Candles.Select(candle => candle.ToResponse())]);
    }
}