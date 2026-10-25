using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Closed history of the test venue for live runtime tests: one candle per step at a flat price, up to the requested end,
///     except in the ranges a test declares closed, which are reported as market closures (ADR 0023, TC3).
/// </summary>
public sealed class FlatHistorySource : IHistoricalCandleSource
{
    private const decimal Price = 100m;

    public List<CandleRangeInternal> Closed { get; } = [];

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public string Source => "flat";

    public Task<DateTimeOffset?> FindFirst(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return Task.FromResult<DateTimeOffset?>(null);
    }

    public Task<HistoricalCandlesInternal> Get(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var candles = new List<Candle>();
        var closures = new List<CandleRangeInternal>();

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            if (Closed.FirstOrDefault(range => range.From <= time && time < range.To) is { } closed)
            {
                DateTimeOffset end = closed.To < to ? closed.To : to;
                closures.Add(new CandleRangeInternal(time, end));
                time = end - timeframe.Duration;
            }
            else
            {
                candles.Add(Candle(time));
            }
        }

        return Task.FromResult(new HistoricalCandlesInternal(candles, closures));
    }

    public static Candle Candle(DateTimeOffset openTime)
    {
        return new Candle(openTime, Price, Price, Price, Price, Volume: 1m);
    }
}