using FluentValidation;

namespace ProfitGraph.Application.Accounts.GetAccountHistory;

internal sealed class GetAccountHistoryQueryValidator : AbstractValidator<GetAccountHistoryQuery>
{
    public GetAccountHistoryQueryValidator()
    {
        RuleFor(query => query.To).GreaterThan(query => query.From).WithErrorCode("invalid_value").WithMessage("The range must end after it starts.");
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, to: 5000).WithErrorCode("invalid_value").WithMessage("The limit must be between 1 and 5000.");
    }
}