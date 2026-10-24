using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Backtesting.RunBacktest;

/// <summary>
///     Executes a queued run in the Worker; the job has the run's identifier.
/// </summary>
public sealed record RunBacktestCommand(Guid RunId) : IBackgroundJobRequest<BacktestCompletedInternal>
{
    public static BackgroundJobKind Kind { get; } = new BackgroundJobKind("backtest");
}