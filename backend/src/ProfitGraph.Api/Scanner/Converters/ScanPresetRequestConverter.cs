using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner;

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

        ScanTradeDirection direction = request.TradeDirection switch
        {
            null or nameof(ScanTradeDirection.Both) => ScanTradeDirection.Both,
            nameof(ScanTradeDirection.Long) => ScanTradeDirection.Long,
            nameof(ScanTradeDirection.Short) => ScanTradeDirection.Short,
            _ => InvalidDirection(errors)
        };

        if (errors.Count > 0)
        {
            throw new ApplicationValidationException(errors);
        }

        return new ScanPresetDefinitionInternal(request.Name, converted, request.SortMetric, request.SortDescending, request.HideStale, direction);
    }

    private static ScanTradeDirection InvalidDirection(List<InputError> errors)
    {
        errors.Add(new InputError("invalid_value", nameof(ScanPresetRequest.TradeDirection), "Use Both, Long or Short."));

        return ScanTradeDirection.Both;
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