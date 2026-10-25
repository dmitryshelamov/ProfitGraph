using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     An instrument at a venue that a bot which should run trades.
/// </summary>
public sealed record LiveInstrument(VenueId Venue, InstrumentSymbol Symbol);