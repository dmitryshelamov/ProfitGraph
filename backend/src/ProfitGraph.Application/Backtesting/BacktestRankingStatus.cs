namespace ProfitGraph.Application.Backtesting;

/// <summary>
///     How a listed run ended: completed with a result, failed or cancelled. Queued and running runs are not listed; the
///     user's active jobs show them.
/// </summary>
public enum BacktestRankingStatus
{
    Completed,
    Failed,
    Cancelled
}