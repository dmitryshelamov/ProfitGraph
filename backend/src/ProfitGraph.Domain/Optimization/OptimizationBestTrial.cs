namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     The top-ranked completed trial so far: its index in the search and its objective value.
/// </summary>
public sealed record OptimizationBestTrial(int TrialIndex, decimal Objective);