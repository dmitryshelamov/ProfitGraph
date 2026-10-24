namespace ProfitGraph.Application.MarketData.Contracts;

public sealed record CandleInternal(DateTimeOffset OpenTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);