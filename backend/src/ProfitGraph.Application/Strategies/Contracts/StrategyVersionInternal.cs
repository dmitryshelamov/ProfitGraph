namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategyVersionInternal(
    Guid Id,
    Guid StrategyId,
    int Number,
    DateTimeOffset CreatedAt,
    StrategyDefinitionInternal Definition,
    GraphLayoutInternal Layout);