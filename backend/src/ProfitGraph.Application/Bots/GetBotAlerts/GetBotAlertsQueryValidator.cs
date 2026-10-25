using FluentValidation;

namespace ProfitGraph.Application.Bots.GetBotAlerts;

internal sealed class GetBotAlertsQueryValidator : AbstractValidator<GetBotAlertsQuery>
{
    public GetBotAlertsQueryValidator()
    {
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, to: 500).WithErrorCode("invalid_value").WithMessage("The limit must be between 1 and 500.");
    }
}