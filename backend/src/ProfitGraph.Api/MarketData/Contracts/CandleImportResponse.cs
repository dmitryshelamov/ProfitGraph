namespace ProfitGraph.Api.MarketData.Contracts;

public sealed record CandleImportResponse(string Venue, string Symbol, string Timeframe, int Accepted, DateTimeOffset From, DateTimeOffset To);