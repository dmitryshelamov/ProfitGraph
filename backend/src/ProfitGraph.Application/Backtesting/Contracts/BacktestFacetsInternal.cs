namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     The values the owner's finished runs take in each filter dimension, most runs first. <paramref name="Quotes" /> counts
///     completed runs with a result, the population the ranking compares; the other dimensions count every finished run.
/// </summary>
public sealed record BacktestFacetsInternal(
    IReadOnlyList<BacktestFacetInternal> Quotes,
    IReadOnlyList<BacktestFacetInternal> Statuses,
    IReadOnlyList<BacktestFacetInternal> Origins,
    IReadOnlyList<BacktestFacetInternal> Venues,
    IReadOnlyList<BacktestFacetInternal> Timeframes,
    IReadOnlyList<BacktestStrategyFacetInternal> Strategies);