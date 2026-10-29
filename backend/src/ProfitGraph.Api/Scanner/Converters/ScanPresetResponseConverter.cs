using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Api.Scanner.Converters;

internal static class ScanPresetResponseConverter
{
    public static ScanPresetResponse ToResponse(this ScanPresetInternal preset)
    {
        return new ScanPresetResponse(
            preset.Id,
            preset.Venue,
            preset.Name,
            [
                .. preset.Conditions.Select(condition => new ScanConditionResponse(
                    condition.Metric,
                    condition.Minimum is { } minimum ? ExactGraphNumberParser.Format(minimum) : null,
                    condition.Maximum is { } maximum ? ExactGraphNumberParser.Format(maximum) : null))
            ],
            preset.SortMetric,
            preset.SortDescending,
            preset.HideStale,
            preset.BuiltIn,
            preset.AlertsEnabled);
    }
}