using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Closed history of the test venue for live runtime tests: one candle per step at a flat price, up to the requested end.
/// </summary>
public sealed class FlatHistorySource : IHistoricalCandleSource
{
    private const decimal Price = 100m;

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public string Source => "flat";

    public Task<DateTimeOffset?> FindFirst(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return Task.FromResult<DateTimeOffset?>(null);
    }

    public Task<HistoricalCandlesInternal> Get(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var candles = new List<Candle>();

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            candles.Add(Candle(time));
        }

        return Task.FromResult(HistoricalCandlesInternal.Contiguous(candles));
    }

    public static Candle Candle(DateTimeOffset openTime)
    {
        return new Candle(openTime, Price, Price, Price, Price, Volume: 1m);
    }
}