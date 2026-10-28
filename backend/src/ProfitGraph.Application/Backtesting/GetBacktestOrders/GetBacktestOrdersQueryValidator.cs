using FluentValidation;

using ProfitGraph.Application.Common.Queries;

namespace ProfitGraph.Application.Backtesting.GetBacktestOrders;

internal sealed class GetBacktestOrdersQueryValidator : AbstractValidator<GetBacktestOrdersQuery>
{
    public GetBacktestOrdersQueryValidator(QueryLimitOptions limits)
    {
        RuleFor(query => query.After).GreaterThanOrEqualTo(0);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.BacktestOrders);
    }
}