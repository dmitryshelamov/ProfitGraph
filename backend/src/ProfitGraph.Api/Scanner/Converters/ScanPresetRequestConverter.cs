using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Api.Scanner.Converters;

internal static class ScanPresetRequestConverter
{
    public static ScanPresetDefinitionInternal ToInternal(this ScanPresetRequest request)
    {
        var errors = new List<InputError>();
        IReadOnlyList<ScanConditionRequest> conditions = request.Conditions ?? [];

        ScanConditionInternal[] converted =
        [
            .. conditions.Select((condition, index) => new ScanConditionInternal(
                condition.Metric,
                Bound(condition.Minimum, $"Conditions[{index}].Minimum", errors),
                Bound(condition.Maximum, $"Conditions[{index}].Maximum", errors)))
        ];

        if (errors.Count > 0)
        {
            throw new ApplicationValidationException(errors);
        }

        return new ScanPresetDefinitionInternal(request.Name, converted, request.SortMetric, request.SortDescending, request.HideStale);
    }

    private static decimal? Bound(string? value, string field, List<InputError> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (ExactGraphNumberParser.TryDecimal(value, out decimal number))
        {
            return number;
        }

        errors.Add(new InputError("invalid_number", field, "Use an exact invariant decimal string."));

        return null;
    }
}