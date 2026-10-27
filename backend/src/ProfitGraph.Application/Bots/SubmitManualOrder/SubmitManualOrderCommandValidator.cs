using FluentValidation;

namespace ProfitGraph.Application.Bots.SubmitManualOrder;

internal sealed class SubmitManualOrderCommandValidator : AbstractValidator<SubmitManualOrderCommand>
{
    public SubmitManualOrderCommandValidator()
    {
        RuleFor(command => command.Side).IsInEnum().WithErrorCode("invalid_value").WithMessage("The side must be Buy or Sell.");
        RuleFor(command => command.Amount).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The amount must be positive.");
        RuleFor(command => command.LimitPrice).GreaterThan(0m).When(command => command.LimitPrice.HasValue).WithErrorCode("invalid_value")
            .WithMessage("The limit price must be positive.");
    }
}