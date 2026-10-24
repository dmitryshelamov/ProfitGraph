using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.Ports;

/// <summary>
///     Signals, after the Worker commits, that a run stored new facts or changed its stage or result (stage 3 plan,
///     S3-11). A signal carries no facts: readers take them from storage.
/// </summary>
public interface IBacktestProgressNotifications
{
    IAsyncEnumerable<BacktestRunId> Listen(CancellationToken cancellationToken);
}