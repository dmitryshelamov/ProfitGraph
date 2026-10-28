using FluentValidation;

using ProfitGraph.Application.Common.Queries;

namespace ProfitGraph.Application.Backtesting.GetBacktestFills;

internal sealed class GetBacktestFillsQueryValidator : AbstractValidator<GetBacktestFillsQuery>
{
    public GetBacktestFillsQueryValidator(QueryLimitOptions limits)
    {
        RuleFor(query => query.After).GreaterThanOrEqualTo(0);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.BacktestFills);
    }
}