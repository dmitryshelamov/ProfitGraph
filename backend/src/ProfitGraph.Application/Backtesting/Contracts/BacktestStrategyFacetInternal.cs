namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     A strategy the owner's runs ran, with its current name and the number of runs.
/// </summary>
public sealed record BacktestStrategyFacetInternal(Guid Id, string Name, int Count);