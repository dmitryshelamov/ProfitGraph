namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     A page of runs; <paramref name="Next" /> is the identifier of the last run, to pass as the cursor of the next page.
/// </summary>
public sealed record BacktestSummaryPageInternal(IReadOnlyList<BacktestSummaryInternal> Items, Guid? Next);