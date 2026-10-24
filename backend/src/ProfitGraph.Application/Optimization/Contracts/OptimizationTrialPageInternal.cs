namespace ProfitGraph.Application.Optimization.Contracts;

/// <summary>
///     Trials in delivery order; <paramref name="Next" /> is the sequence to pass as the cursor of the next page, or null at
///     the end.
/// </summary>
public sealed record OptimizationTrialPageInternal(IReadOnlyList<OptimizationTrialInternal> Items, long? Next);