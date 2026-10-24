namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     The column the run ranking is ordered by; ties are broken by start time and identifier in the same direction.
/// </summary>
public enum BacktestRankingSort
{
    ReturnPercent,
    TotalPnl,
    MaxDrawdownPercent,
    CreatedAt
}