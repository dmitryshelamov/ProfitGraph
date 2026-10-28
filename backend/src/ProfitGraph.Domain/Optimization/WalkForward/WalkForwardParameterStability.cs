using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Domain.Optimization.WalkForward;

public sealed record WalkForwardParameterStability(
    PortReference Reference,
    IReadOnlyList<WalkForwardParameterWinner> Winners,
    int DistinctCount,
    decimal? RangeInSteps);