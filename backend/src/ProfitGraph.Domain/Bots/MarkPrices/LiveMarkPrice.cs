using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Bots.MarkPrices;

/// <summary>
///     The latest display price of an instrument at a venue (S5-19). It never reaches the engine; a bot's position shows
///     it instead of the last candle's close only while it is fresh.
/// </summary>
public sealed record LiveMarkPrice(VenueId Venue, InstrumentSymbol Symbol, decimal Price, DateTimeOffset ObservedAt)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Whether the observation displaces a candle that closed at <paramref name="candleClosedAt" />: it is later than
    ///     the close and at most five minutes old.
    /// </summary>
    public bool Displaces(DateTimeOffset candleClosedAt, DateTimeOffset now)
    {
        return ObservedAt > candleClosedAt && now - ObservedAt <= MaxAge;
    }
}