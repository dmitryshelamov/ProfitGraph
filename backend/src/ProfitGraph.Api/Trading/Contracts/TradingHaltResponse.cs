namespace ProfitGraph.Api.Trading.Contracts;

public sealed record TradingHaltResponse(bool IsHalted, DateTimeOffset UpdatedAt);