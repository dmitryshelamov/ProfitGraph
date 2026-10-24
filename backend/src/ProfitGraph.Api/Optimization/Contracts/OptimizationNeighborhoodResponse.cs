namespace ProfitGraph.Api.Optimization.Contracts;

/// <param name="Evaluated">Evaluated grid neighbors of the trial.</param>
/// <param name="Eligible">Neighbors with an objective.</param>
/// <param name="Mean">The mean objective of the eligible neighbors.</param>
public sealed record OptimizationNeighborhoodResponse(int Evaluated, int Eligible, string? Mean);