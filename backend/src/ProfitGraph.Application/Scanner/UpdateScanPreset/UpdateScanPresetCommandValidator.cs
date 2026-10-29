using FluentValidation;

using ProfitGraph.Application.Scanner.Validators;

namespace ProfitGraph.Application.Scanner.UpdateScanPreset;

internal sealed class UpdateScanPresetCommandValidator : AbstractValidator<UpdateScanPresetCommand>
{
    public UpdateScanPresetCommandValidator()
    {
        RuleFor(command => command.Definition).NotNull().SetValidator(new ScanPresetDefinitionInternalValidator());
    }
}