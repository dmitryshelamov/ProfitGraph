using FluentValidation;

namespace ProfitGraph.Application.Bots.GetBotPosition;

internal sealed class GetBotPositionQueryValidator : AbstractValidator<GetBotPositionQuery>
{
    public GetBotPositionQueryValidator()
    {
        RuleFor(query => query.CycleLimit).InclusiveBetween(from: 1, to: 200).WithErrorCode("invalid_value").WithMessage("The cycle limit must be between 1 and 200.");
    }
}