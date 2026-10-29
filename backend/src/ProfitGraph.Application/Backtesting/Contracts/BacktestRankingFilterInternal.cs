namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     Which of the user's finished runs a list shows. Completed runs are compared in one quote currency, so
///     <paramref name="Quote" /> is required for them and optional otherwise; <paramref name="Search" /> matches a part of
///     the symbol or the strategy name, ignoring case; <paramref name="From" /> and <paramref name="To" /> bound the start
///     time (inclusive and exclusive); the result bounds apply to completed runs only.
/// </summary>
public sealed record BacktestRankingFilterInternal(
    BacktestRankingStatus Status,
    string? Quote,
    Guid? StrategyId = null,
    string? Venue = null,
    string? Symbol = null,
    string? Search = null,
    string? Timeframe = null,
    BacktestRankingOrigin? Origin = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    decimal? MinReturnPercent = null,
    decimal? MaxDrawdownPercent = null);