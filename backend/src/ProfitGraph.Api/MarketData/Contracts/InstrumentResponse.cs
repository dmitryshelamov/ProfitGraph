namespace ProfitGraph.Api.MarketData.Contracts;

public sealed record InstrumentResponse(string Venue, string Symbol, string BaseAsset, string QuoteAsset);