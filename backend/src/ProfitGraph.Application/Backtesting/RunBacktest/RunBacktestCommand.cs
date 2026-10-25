using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Backtesting.RunBacktest;

/// <summary>
///     Executes a queued run in the Worker; the job has the run's identifier and the owner's credential for market data,
///     if any (ADR 0023, TC6).
/// </summary>
public sealed record RunBacktestCommand(Guid RunId, Guid? CredentialId = null) : IBackgroundJobRequest<BacktestCompletedInternal>
{
    public static BackgroundJobKind Kind { get; } = new BackgroundJobKind("backtest");
}