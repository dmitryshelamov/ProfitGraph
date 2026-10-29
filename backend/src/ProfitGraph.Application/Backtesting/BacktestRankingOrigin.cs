namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     Who started a listed run: the user, or an optimization that confirmed its finalists.
/// </summary>
public enum BacktestRankingOrigin
{
    Manual,
    Optimization
}