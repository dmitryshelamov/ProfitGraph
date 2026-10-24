using ProfitGraph.Domain.Optimization;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

/// <summary>
///     The frozen configuration as stored in <c>optimizations.configuration</c>.
/// </summary>
internal sealed class OptimizationConfigurationDb
{
    public BacktestConfigurationDb Backtest { get; init; } = null!;
    public Dictionary<string, OptimizationRangeDb> Ranges { get; init; } = null!;
    public OptimizationSearchMethod Method { get; init; }
    public OptimizationObjective Objective { get; init; }
    public int Budget { get; init; }
    public ulong Seed { get; init; }
    public int FinalistCount { get; init; }
    public long MinExecutions { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public WalkForwardSettingsDb? WalkForward { get; init; }
}