namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestDiagnosticInternal(
    long Sequence,
    string Code,
    string Message,
    string? NodeId,
    int? CandleIndex,
    string? Tag,
    string? RejectionReason);