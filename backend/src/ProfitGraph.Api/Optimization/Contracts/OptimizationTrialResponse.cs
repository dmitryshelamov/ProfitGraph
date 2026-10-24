namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationTrialResponse(
    long Sequence,
    int TrialIndex,
    IReadOnlyDictionary<string, string> Parameters,
    string Status,
    string? FailureCode,
    string? Objective,
    string? ReturnPercent,
    string? TotalPnl,
    string? MaxDrawdownPercent,
    long Executions,
    long ElapsedMs);