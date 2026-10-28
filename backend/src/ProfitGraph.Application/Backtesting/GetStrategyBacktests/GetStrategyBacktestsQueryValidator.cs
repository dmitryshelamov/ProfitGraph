using FluentValidation;

using ProfitGraph.Application.Common.Queries;

namespace ProfitGraph.Application.Backtesting.GetStrategyBacktests;

internal sealed class GetStrategyBacktestsQueryValidator : AbstractValidator<GetStrategyBacktestsQuery>
{
    public GetStrategyBacktestsQueryValidator(QueryLimitOptions limits)
    {
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.StrategyBacktests);
    }
}