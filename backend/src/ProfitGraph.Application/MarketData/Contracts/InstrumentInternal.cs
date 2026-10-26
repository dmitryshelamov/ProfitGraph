namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     A tradable pair of a venue, for picking one. <see cref="Appearance" /> is set when the venue can describe it.
/// </summary>
public sealed record InstrumentInternal(string Venue, string Symbol, string BaseAsset, string QuoteAsset, InstrumentAppearanceInternal? Appearance = null);
