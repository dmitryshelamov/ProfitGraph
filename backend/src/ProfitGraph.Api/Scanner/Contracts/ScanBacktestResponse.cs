namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A scanner backtest (ADR 0059, section 6). Until <see cref="CompletedAt" /> is set, follow its job; then the summary and the
///     newest signals (at most 10,000, <see cref="SignalsTruncated" /> when there were more) are present.
/// </summary>
public sealed record ScanBacktestResponse(
    Guid Id,
    string Venue,
    string PresetName,
    IReadOnlyList<ScanConditionResponse> Conditions,
    bool HideStale,
    DateTimeOffset From,
    DateTimeOffset To,
    Guid JobId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int? Pairs,
    IReadOnlyList<string>? FailedPairs,
    ScanStudySummaryResponse? Summary,
    IReadOnlyList<ScanSignalResponse>? Signals,
    bool SignalsTruncated);