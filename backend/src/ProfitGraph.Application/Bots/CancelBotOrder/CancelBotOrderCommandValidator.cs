using FluentValidation;

namespace ProfitGraph.Application.Bots.CancelBotOrder;

internal sealed class CancelBotOrderCommandValidator : AbstractValidator<CancelBotOrderCommand>
{
    public CancelBotOrderCommandValidator()
    {
        RuleFor(command => command.EngineOrderId).GreaterThan(0).WithErrorCode("invalid_value").WithMessage("The engine order identifier must be positive.");
    }
}