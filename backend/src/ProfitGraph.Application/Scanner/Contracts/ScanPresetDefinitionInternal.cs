namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     What a user sets in a preset: its name, conditions, sort and whether pairs without recent trades are hidden.
/// </summary>
public sealed record ScanPresetDefinitionInternal(
    string Name,
    IReadOnlyList<ScanConditionInternal> Conditions,
    string SortMetric,
    bool SortDescending,
    bool HideStale);