using ProfitGraph.Engine.Market;

namespace ProfitGraph.Domain.MarketData;

/// <summary>
///     One stored history: an instrument of a venue on one timeframe.
/// </summary>
public sealed record CandleSeries(VenueId Venue, InstrumentSymbol Symbol, Timeframe Timeframe)
{
    public VenueId Venue { get; } = Venue;
    public InstrumentSymbol Symbol { get; } = Symbol ?? throw new ArgumentNullException(nameof(Symbol));
    public Timeframe Timeframe { get; } = Timeframe ?? throw new ArgumentNullException(nameof(Timeframe));
}