namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationBestTrialResponse(int TrialIndex, string Objective);