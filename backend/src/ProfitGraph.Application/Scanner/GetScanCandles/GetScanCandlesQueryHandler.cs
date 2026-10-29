using MediatR;

using ProfitGraph.Application.Common.Queries;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Application.MarketData.Converters;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Scanner.GetScanCandles;

internal sealed class GetScanCandlesQueryHandler(
    IEnumerable<IMarketScanSource> sources,
    VenueRegistry venues,
    CandleChartReader chart,
    QueryLimitOptions limits) : IRequestHandler<GetScanCandlesQuery, ScanCandlesInternal>
{
    public async Task<ScanCandlesInternal> Handle(GetScanCandlesQuery query, CancellationToken cancellationToken)
    {
        sources.Find(query.Venue);
        CandleSeries series = venues.ResolveSeries(query.Venue, query.Symbol, query.Timeframe);
        CandleChartPage page = await chart.Read(series, query.From, query.To, limits.ScanChartCandles, cancellationToken);

        return new ScanCandlesInternal(
            series.Venue.Value,
            series.Symbol.Value,
            series.Timeframe.Key,
            page.From,
            page.To,
            page.LatestClosed,
            page.Status,
            page.ErrorCode,
            [.. page.Candles.Select(candle => candle.ToInternal())]);
    }
}