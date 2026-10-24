using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Lowering;

public sealed record StrategyLoweringResult(StrategyGraph Graph, IReadOnlyDictionary<string, StrategySource> Sources);