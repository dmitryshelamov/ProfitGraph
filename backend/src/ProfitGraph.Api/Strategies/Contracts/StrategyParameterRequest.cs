namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyParameterRequest(string Name, string ValueType, string DefaultValue, string? DisplayUnit = null);