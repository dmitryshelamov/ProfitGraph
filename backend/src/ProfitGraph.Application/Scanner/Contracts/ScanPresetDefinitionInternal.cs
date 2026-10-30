using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     What a user sets in a preset: its name, conditions, sort, whether pairs without recent trades are hidden and which trade cards
///     its pairs offer.
/// </summary>
public sealed record ScanPresetDefinitionInternal(
    string Name,
    IReadOnlyList<ScanConditionInternal> Conditions,
    string SortMetric,
    bool SortDescending,
    bool HideStale,
    ScanTradeDirection TradeDirection = ScanTradeDirection.Both);