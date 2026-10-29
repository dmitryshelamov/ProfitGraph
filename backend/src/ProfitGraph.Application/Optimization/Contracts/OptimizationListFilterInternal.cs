using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Contracts;

/// <summary>
///     Which of the user's optimizations a list shows; a null value filters nothing. <paramref name="Search" /> matches a
///     part of the symbol or the strategy name, ignoring case.
/// </summary>
public sealed record OptimizationListFilterInternal(
    OptimizationListState? State = null,
    Guid? StrategyId = null,
    string? Venue = null,
    string? Search = null,
    string? Timeframe = null,
    OptimizationSearchMethod? Method = null);