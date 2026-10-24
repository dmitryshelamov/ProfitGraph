using FluentValidation;

namespace ProfitGraph.Application.Bots.ChangeBotParameters;

internal sealed class ChangeBotParametersCommandValidator : AbstractValidator<ChangeBotParametersCommand>
{
    public ChangeBotParametersCommandValidator()
    {
        RuleFor(command => command.AllocationCap).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The allocation cap must be positive.");
        RuleFor(command => command.Parameters).NotNull().WithErrorCode(BotErrorCodes.ParametersMismatch).WithMessage("Give a value for every strategy parameter.");
    }
}