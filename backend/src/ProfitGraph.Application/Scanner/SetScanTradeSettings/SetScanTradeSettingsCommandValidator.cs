using FluentValidation;

namespace ProfitGraph.Application.Scanner.SetScanTradeSettings;

internal sealed class SetScanTradeSettingsCommandValidator : AbstractValidator<SetScanTradeSettingsCommand>
{
    public SetScanTradeSettingsCommandValidator()
    {
        RuleFor(command => command.CredentialId).NotEmpty().WithErrorCode("required").WithMessage("Choose a credential.");
        RuleFor(command => command.Risk).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The risk is positive.");
        RuleFor(command => command.MaxAmount).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The largest amount is positive.");
    }
}