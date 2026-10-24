using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategySummaryInternal(Guid Id, string Name, int CurrentVersion, DateTimeOffset UpdatedAt, StrategyTemplateKind? TemplateKind);