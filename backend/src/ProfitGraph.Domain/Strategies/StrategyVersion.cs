using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     An immutable saved definition with its editor layout; the layout never affects execution.
/// </summary>
public sealed record StrategyVersion(
    StrategyVersionId Id,
    StrategyId StrategyId,
    StrategyVersionNumber Number,
    StrategyDefinition Definition,
    GraphLayout Layout,
    DateTimeOffset CreatedAt);