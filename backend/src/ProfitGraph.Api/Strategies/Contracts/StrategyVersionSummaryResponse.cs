namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyVersionSummaryResponse(Guid Id, int Number, DateTimeOffset CreatedAt);