using FluentValidation;

namespace ProfitGraph.Application.Bots.ChangeBotRiskLimits;

internal sealed class ChangeBotRiskLimitsCommandValidator : AbstractValidator<ChangeBotRiskLimitsCommand>
{
    public ChangeBotRiskLimitsCommandValidator()
    {
        RuleFor(command => command.MaxOrderQuote).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The per-order limit must be positive.");
        RuleFor(command => command.DailyLossLimitQuote).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The daily loss limit must be positive.");
    }
}