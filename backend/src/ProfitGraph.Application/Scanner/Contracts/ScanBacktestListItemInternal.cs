namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     One of the owner's scanner backtests in a list: its preset, period, job and, once complete, how many signals it found.
/// </summary>
public sealed record ScanBacktestListItemInternal(
    Guid Id,
    string PresetName,
    DateTimeOffset From,
    DateTimeOffset To,
    Guid JobId,
    string? JobStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int? Signals);