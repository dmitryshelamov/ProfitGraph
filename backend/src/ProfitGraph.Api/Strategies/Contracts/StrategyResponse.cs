namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyResponse(
    Guid Id,
    string Name,
    int CurrentVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    StrategyDefinitionResponse Definition,
    GraphLayoutResponse Layout);