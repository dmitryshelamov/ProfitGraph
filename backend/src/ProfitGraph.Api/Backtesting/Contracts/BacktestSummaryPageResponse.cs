namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     A page of runs; pass <paramref name="Next" /> as <c>after</c> to read the following one. Null means the end.
/// </summary>
public sealed record BacktestSummaryPageResponse(IReadOnlyList<BacktestSummaryResponse> Items, Guid? Next);