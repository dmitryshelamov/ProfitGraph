namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A scanner preset: the user's own with its identifier, or a built-in one, read-only and without an identifier.
/// </summary>
public sealed record ScanPresetResponse(
    Guid? Id,
    string Venue,
    string Name,
    IReadOnlyList<ScanConditionResponse> Conditions,
    string SortMetric,
    bool SortDescending,
    bool HideStale,
    bool BuiltIn);