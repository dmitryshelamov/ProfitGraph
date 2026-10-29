using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.Converters;

internal static class ScanPresetInternalConverter
{
    public static ScanPresetInternal ToInternal(this ScanPreset preset)
    {
        return new ScanPresetInternal(
            preset.Id.Value,
            preset.Venue.Value,
            preset.Name.Value,
            [.. preset.Conditions.Select(condition => new ScanConditionInternal(condition.Metric.ToString(), condition.Minimum, condition.Maximum))],
            preset.Sort.Metric.ToString(),
            preset.Sort.Descending,
            preset.HideStale,
            BuiltIn: false);
    }

    /// <summary>
    ///     The domain conditions of a validated definition.
    /// </summary>
    public static IReadOnlyList<ScanCondition> ToDomainConditions(this ScanPresetDefinitionInternal definition)
    {
        return [.. definition.Conditions.Select(condition => new ScanCondition(Enum.Parse<ScanMetric>(condition.Metric), condition.Minimum, condition.Maximum))];
    }

    public static ScanSort ToDomainSort(this ScanPresetDefinitionInternal definition)
    {
        return new ScanSort(Enum.Parse<ScanMetric>(definition.SortMetric), definition.SortDescending);
    }
}