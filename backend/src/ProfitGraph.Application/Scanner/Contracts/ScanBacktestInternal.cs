using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A scanner backtest (ADR 0059, section 6) with its result once its job completed it; <see cref="Summary" /> and
///     <see cref="Signals" /> are null until then.
/// </summary>
public sealed record ScanBacktestInternal(
    Guid Id,
    string Venue,
    string PresetName,
    IReadOnlyList<ScanConditionInternal> Conditions,
    bool HideStale,
    DateTimeOffset From,
    DateTimeOffset To,
    Guid JobId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int? Pairs,
    IReadOnlyList<string>? FailedPairs,
    ScanStudySummary? Summary,
    IReadOnlyList<ScanSignal>? Signals,
    bool SignalsTruncated);