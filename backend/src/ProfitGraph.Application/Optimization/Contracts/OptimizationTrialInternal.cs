namespace ProfitGraph.Application.Optimization.Contracts;

/// <summary>
///     A stored trial. <paramref name="Sequence" /> is its delivery order, from 1, and the cursor of trial pages;
///     <paramref name="TrialIndex" /> is its position in the search. Parameters are the ranged values by name.
/// </summary>
public sealed record OptimizationTrialInternal(
    long Sequence,
    int TrialIndex,
    IReadOnlyDictionary<string, decimal> Parameters,
    string Status,
    string? FailureCode,
    decimal? Objective,
    decimal? ReturnPercent,
    decimal? TotalPnl,
    decimal? MaxDrawdownPercent,
    long Executions,
    long ElapsedMs);