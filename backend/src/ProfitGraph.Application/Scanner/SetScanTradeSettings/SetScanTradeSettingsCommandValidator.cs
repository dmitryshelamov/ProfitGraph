using FluentValidation;

namespace ProfitGraph.Application.Scanner.SetScanTradeSettings;

internal sealed class SetScanTradeSettingsCommandValidator : AbstractValidator<SetScanTradeSettingsCommand>
{
    public SetScanTradeSettingsCommandValidator()
    {
        RuleFor(command => command.CredentialId).NotEmpty().WithErrorCode("required").WithMessage("Choose a credential.");
        RuleFor(command => command.Risk).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The risk is positive.");
        RuleFor(command => command.MaxAmount).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The largest amount is positive.");
        RuleFor(command => command.Leverage)
            .Must(leverage => leverage is not { } value || (value is >= 1m and <= 100m && decimal.Round(value, decimals: 2) == value))
            .WithErrorCode("invalid_value")
            .WithMessage("The leverage is from 1 to 100 with at most two decimals.");
    }
}