using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestDiagnostics;

/// <summary>
///     Diagnostics after a sequence.
/// </summary>
public sealed record GetBacktestDiagnosticsQuery(Guid UserId, Guid Id, long After, int Limit) : IQuery<BacktestPageInternal<BacktestDiagnosticInternal>>;