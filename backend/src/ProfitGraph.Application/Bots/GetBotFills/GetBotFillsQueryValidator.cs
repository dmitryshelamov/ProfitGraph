using FluentValidation;

namespace ProfitGraph.Application.Bots.GetBotFills;

internal sealed class GetBotFillsQueryValidator : AbstractValidator<GetBotFillsQuery>
{
    public GetBotFillsQueryValidator()
    {
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, to: 200).WithErrorCode("invalid_value").WithMessage("The limit must be between 1 and 200.");
    }
}