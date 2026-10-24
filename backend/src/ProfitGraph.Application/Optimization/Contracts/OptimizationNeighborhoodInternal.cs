namespace ProfitGraph.Application.Optimization.Contracts;

/// <summary>
///     How a trial's evaluated grid neighbors scored: how many were evaluated, how many had an objective, and their mean.
/// </summary>
public sealed record OptimizationNeighborhoodInternal(int Evaluated, int Eligible, decimal? Mean);