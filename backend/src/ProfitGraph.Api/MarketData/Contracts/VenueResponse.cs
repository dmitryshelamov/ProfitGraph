namespace ProfitGraph.Api.MarketData.Contracts;

public sealed record VenueResponse(string Id, string Name, IReadOnlyList<string> Timeframes);