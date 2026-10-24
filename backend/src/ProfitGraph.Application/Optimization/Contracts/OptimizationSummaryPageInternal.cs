namespace ProfitGraph.Application.Optimization.Contracts;

/// <summary>
///     A page of optimizations, newest first; <paramref name="Next" /> is the identifier of the last one, to pass as the
///     cursor of the next page.
/// </summary>
public sealed record OptimizationSummaryPageInternal(IReadOnlyList<OptimizationSummaryInternal> Items, Guid? Next);