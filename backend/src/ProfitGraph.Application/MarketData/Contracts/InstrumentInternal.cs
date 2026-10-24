namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     A tradable pair of a venue, for picking one.
/// </summary>
public sealed record InstrumentInternal(string Venue, string Symbol, string BaseAsset, string QuoteAsset);