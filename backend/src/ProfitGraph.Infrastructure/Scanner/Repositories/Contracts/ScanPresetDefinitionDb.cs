namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

/// <summary>
///     The stored JSON of a preset's conditions, sort and stale switch.
/// </summary>
internal sealed record ScanPresetDefinitionDb(
    int FormatVersion,
    IReadOnlyList<ScanPresetConditionDb> Conditions,
    string SortMetric,
    bool SortDescending,
    bool HideStale);