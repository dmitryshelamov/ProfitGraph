namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyTemplateResponse(string Kind, string Name, string Description, StrategyDefinitionResponse Definition, GraphLayoutResponse Layout);