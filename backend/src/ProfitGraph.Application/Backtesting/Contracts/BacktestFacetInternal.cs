namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     One value of a filter dimension and the number of the owner's runs that have it.
/// </summary>
public sealed record BacktestFacetInternal(string Value, int Count);