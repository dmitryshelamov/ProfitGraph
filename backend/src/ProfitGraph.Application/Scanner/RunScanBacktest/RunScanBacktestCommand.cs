using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Scanner.RunScanBacktest;

public sealed record RunScanBacktestCommand(Guid BacktestId) : IBackgroundJobRequest<ScanBacktestCompletedInternal>
{
    public static BackgroundJobKind Kind { get; } = new BackgroundJobKind("scan_backtest");
}