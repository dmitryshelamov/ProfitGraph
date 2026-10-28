namespace ProfitGraph.Domain.Optimization.Search;

public sealed record OptimizationNeighborhoodScore(int Evaluated, int Eligible, decimal? Mean);