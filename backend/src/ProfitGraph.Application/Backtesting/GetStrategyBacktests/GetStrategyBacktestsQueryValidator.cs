using FluentValidation;

namespace ProfitGraph.Application.Backtesting.GetStrategyBacktests;

internal sealed class GetStrategyBacktestsQueryValidator : AbstractValidator<GetStrategyBacktestsQuery>
{
    public GetStrategyBacktestsQueryValidator()
    {
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, GetStrategyBacktestsQuery.MaxLimit);
    }
}