using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.Execution;

/// <summary>
///     A diagnostic in the order the run reported it, numbered from 1.
/// </summary>
public sealed record BacktestDiagnosticRecord(long Sequence, BacktestDiagnostic Diagnostic);