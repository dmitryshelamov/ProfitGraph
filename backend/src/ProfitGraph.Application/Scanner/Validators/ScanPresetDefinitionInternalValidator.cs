using FluentValidation;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.Validators;

/// <summary>
///     A name within its length, at most <see cref="ScanPreset.MaxConditions" /> conditions and known metric names; bounds are
///     checked by <see cref="ScanCondition" />.
/// </summary>
internal sealed class ScanPresetDefinitionInternalValidator : AbstractValidator<ScanPresetDefinitionInternal>
{
    public ScanPresetDefinitionInternalValidator()
    {
        RuleFor(definition => definition.Name).NotEmpty().MaximumLength(ScanPresetName.MaxLength);
        RuleFor(definition => definition.Conditions).NotNull().Must(conditions => conditions.Count <= ScanPreset.MaxConditions);
        RuleForEach(definition => definition.Conditions).Must(condition => IsMetric(condition.Metric)).WithMessage("Unknown scanner metric.");
        RuleFor(definition => definition.SortMetric).Must(IsMetric).WithMessage("Unknown scanner metric.");
    }

    private static bool IsMetric(string? metric)
    {
        return Enum.GetNames<ScanMetric>().Contains(metric, StringComparer.Ordinal);
    }
}