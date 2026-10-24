using FluentValidation;

namespace ProfitGraph.Application.Bots.CreateBot;

internal sealed class CreateBotCommandValidator : AbstractValidator<CreateBotCommand>
{
    public CreateBotCommandValidator()
    {
        RuleFor(command => command.AllocationCap).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The allocation cap must be positive.");
        RuleFor(command => command.MaxOrderQuote).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The per-order limit must be positive.");
        RuleFor(command => command.DailyLossLimitQuote).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The daily loss limit must be positive.");
        RuleFor(command => command.Parameters).NotNull().WithErrorCode(BotErrorCodes.ParametersMismatch).WithMessage("Give a value for every strategy parameter.");
    }
}