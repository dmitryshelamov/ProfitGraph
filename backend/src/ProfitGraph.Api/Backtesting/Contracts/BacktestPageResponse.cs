namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     A bounded page; pass <paramref name="Next" /> as <c>after</c> to read the following one. Null means the end.
/// </summary>
public sealed record BacktestPageResponse<T>(IReadOnlyList<T> Items, long? Next);