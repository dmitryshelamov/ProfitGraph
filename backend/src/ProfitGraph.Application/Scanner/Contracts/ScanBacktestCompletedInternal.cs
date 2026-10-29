using ProfitGraph.Application.Jobs;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The result of a scanner backtest's job: the backtest and how many signals it found.
/// </summary>
public sealed record ScanBacktestCompletedInternal(Guid BacktestId, int Signals) : IBackgroundJobResult;