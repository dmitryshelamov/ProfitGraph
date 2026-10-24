namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategyInternal(
    Guid Id,
    string Name,
    int CurrentVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    StrategyDefinitionInternal Definition,
    GraphLayoutInternal Layout);