using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Structured replay explanation or failure at the already reached market cursor.
/// </summary>
public sealed record BacktestDiagnostic(
    string Code,
    string Message,
    string? NodeId = null,
    int? CandleIndex = null,
    long? OccurrenceId = null,
    long? CausationId = null,
    string? Tag = null,
    RejectionReason? RejectionReason = null,
    long? AttemptId = null);