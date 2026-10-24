using MediatR;

using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Converters;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.GetCandles;

/// <summary>
///     Reads stored history only; it never calls a venue.
/// </summary>
internal sealed class GetCandlesQueryHandler(VenueRegistry venues, ICandleRepository repository)
    : IRequestHandler<GetCandlesQuery, IReadOnlyList<CandleInternal>>
{
    public async Task<IReadOnlyList<CandleInternal>> Handle(GetCandlesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Candle> candles = await repository.Get(
            venues.ResolveSeries(request.Venue, request.Symbol, request.Timeframe),
            request.From,
            request.To,
            request.Limit,
            cancellationToken);

        return [.. candles.Select(candle => candle.ToInternal())];
    }
}