using FluentValidation;

using ProfitGraph.Application.Scanner.Validators;

namespace ProfitGraph.Application.Scanner.CreateScanPreset;

internal sealed class CreateScanPresetCommandValidator : AbstractValidator<CreateScanPresetCommand>
{
    public CreateScanPresetCommandValidator()
    {
        RuleFor(command => command.Definition).NotNull().SetValidator(new ScanPresetDefinitionInternalValidator());
    }
}