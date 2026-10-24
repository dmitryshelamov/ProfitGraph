namespace ProfitGraph.Domain.Optimization;

public sealed record OptimizationNeighborhoodScore(int Evaluated, int Eligible, decimal? Mean);