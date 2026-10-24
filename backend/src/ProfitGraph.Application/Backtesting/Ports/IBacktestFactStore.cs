using ProfitGraph.Application.Backtesting.Execution;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.Ports;

/// <summary>
///     Immutable facts of runs (ADR 0012). A batch is written inside the caller's transaction and is never changed.
/// </summary>
public interface IBacktestFactStore
{
    Task Append(BacktestRunId runId, BacktestFactBatch batch, CancellationToken cancellationToken);
}