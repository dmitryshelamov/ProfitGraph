namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     A bounded page in a stable order; <paramref name="Next" /> is the cursor of the following page, or null at the end.
/// </summary>
public sealed record BacktestPageInternal<T>(IReadOnlyList<T> Items, long? Next);