namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A preset the user saves; <see cref="TradeDirection" /> is <c>Both</c>, <c>Long</c> or <c>Short</c>, <c>Both</c> when it is left out
///     (ADR 0069).
/// </summary>
public sealed record ScanPresetRequest(
    string Name,
    IReadOnlyList<ScanConditionRequest> Conditions,
    string SortMetric,
    bool SortDescending,
    bool HideStale,
    string? TradeDirection = null);