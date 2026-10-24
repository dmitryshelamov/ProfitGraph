using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     Bybit history for live-feed tests: one candle per timeframe step with a known close.
/// </summary>
internal sealed class FakeBybitHistory : IHistoricalCandleSource
{
    public int Reads { get; private set; }

    public VenueId Venue { get; } = new VenueId("bybit");

    public string Source => "fake";

    public Task<DateTimeOffset?> FindFirst(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return Task.FromResult<DateTimeOffset?>(null);
    }

    public Task<IReadOnlyList<Candle>> Get(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        Reads++;
        var candles = new List<Candle>();

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            candles.Add(new Candle(time, Open: 100m, High: 110m, Low: 90m, Close: 101m, Volume: 1m));
        }

        return Task.FromResult<IReadOnlyList<Candle>>(candles);
    }
}