namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyParameterResponse(string Name, string ValueType, string DefaultValue, string? DisplayUnit = null);