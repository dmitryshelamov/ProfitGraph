namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyValueRequest(string Name, ValueExpressionRequest Expression);