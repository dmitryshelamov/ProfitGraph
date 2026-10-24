namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyVersionResponse(
    Guid Id,
    Guid StrategyId,
    int Number,
    DateTimeOffset CreatedAt,
    StrategyDefinitionResponse Definition,
    GraphLayoutResponse Layout);