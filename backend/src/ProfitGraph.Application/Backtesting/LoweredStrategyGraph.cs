using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     A strategy graph lowered with fixed parameter values, with the block catalog to compile it against.
/// </summary>
public sealed record LoweredStrategyGraph(StrategyGraph Graph, IReadOnlyList<INodeDefinition> Catalog);