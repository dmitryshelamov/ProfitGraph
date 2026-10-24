namespace ProfitGraph.Application.MarketData.Contracts;

public sealed record VenueInternal(string Id, string Name, IReadOnlyList<string> Timeframes);