using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Domain.Strategies;

public sealed record StrategySummary(
    StrategyId Id,
    StrategyName Name,
    StrategyVersionNumber CurrentVersion,
    DateTimeOffset UpdatedAt,
    StrategyTemplateKind? TemplateKind);