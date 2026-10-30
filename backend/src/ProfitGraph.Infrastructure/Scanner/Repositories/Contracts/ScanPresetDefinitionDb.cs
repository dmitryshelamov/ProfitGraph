namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

/// <summary>
///     The stored JSON of a preset's conditions, sort, stale switch and trade direction; a definition stored before the direction
///     (ADR 0069) has none and trades both ways.
/// </summary>
internal sealed record ScanPresetDefinitionDb(
    int FormatVersion,
    IReadOnlyList<ScanPresetConditionDb> Conditions,
    string SortMetric,
    bool SortDescending,
    bool HideStale,
    string? TradeDirection = null);