using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategyTemplateInternal(
    StrategyTemplateKind Kind,
    string Name,
    string Description,
    StrategyDefinitionInternal Definition,
    GraphLayoutInternal Layout);