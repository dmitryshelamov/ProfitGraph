using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Domain.Strategies;

public sealed record StrategySummary(Guid Id, string Name, int CurrentVersion, DateTimeOffset UpdatedAt, StrategyTemplateKind? TemplateKind);