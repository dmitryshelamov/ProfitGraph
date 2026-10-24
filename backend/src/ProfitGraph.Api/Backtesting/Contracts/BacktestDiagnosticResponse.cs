namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestDiagnosticResponse(
    long Sequence,
    string Code,
    string Message,
    string? NodeId,
    int? CandleIndex,
    string? Tag,
    string? RejectionReason);