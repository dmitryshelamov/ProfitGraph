namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A scanner preset of a venue: the user's own, with its identifier, or one the venue module builds in, read-only and without
///     an identifier (ADR 0059, section 4).
/// </summary>
public sealed record ScanPresetInternal(
    Guid? Id,
    string Venue,
    string Name,
    IReadOnlyList<ScanConditionInternal> Conditions,
    string SortMetric,
    bool SortDescending,
    bool HideStale,
    bool BuiltIn);