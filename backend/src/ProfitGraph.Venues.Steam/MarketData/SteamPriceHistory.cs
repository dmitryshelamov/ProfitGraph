using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     The median candles of one item: native and estimated daily candles, and hourly candles from the start of Steam's
///     hourly zone. A period without sales has no candle.
/// </summary>
internal sealed record SteamPriceHistory(IReadOnlyList<Candle> Daily, IReadOnlyList<Candle> Hourly, DateTimeOffset HourlyStart)
{
    public IReadOnlyList<Candle> For(Timeframe timeframe)
    {
        return timeframe.Duration == TimeSpan.FromDays(1) ? Daily : Hourly;
    }
}