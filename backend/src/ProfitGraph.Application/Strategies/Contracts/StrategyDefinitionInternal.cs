using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategyDefinitionInternal(
    IReadOnlyList<StrategyParameterInternal> Parameters,
    IReadOnlyList<StrategyValueInternal> Values,
    IReadOnlyList<StrategyBlockInternal> Blocks,
    IReadOnlyList<StrategyWireInternal> Wires,
    StrategyTemplateKind? TemplateKind = null,
    PositionDirection Direction = PositionDirection.Long);