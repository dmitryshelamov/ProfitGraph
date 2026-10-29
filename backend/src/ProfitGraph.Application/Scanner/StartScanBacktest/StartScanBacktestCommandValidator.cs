using FluentValidation;

using ProfitGraph.Application.Scanner.Validators;

namespace ProfitGraph.Application.Scanner.StartScanBacktest;

internal sealed class StartScanBacktestCommandValidator : AbstractValidator<StartScanBacktestCommand>
{
    public StartScanBacktestCommandValidator()
    {
        RuleFor(command => command.Definition).NotNull().SetValidator(new ScanPresetDefinitionInternalValidator());
    }
}