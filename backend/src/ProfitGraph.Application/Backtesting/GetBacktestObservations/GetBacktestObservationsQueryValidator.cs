using FluentValidation;

using ProfitGraph.Application.Common.Queries;

namespace ProfitGraph.Application.Backtesting.GetBacktestObservations;

internal sealed class GetBacktestObservationsQueryValidator : AbstractValidator<GetBacktestObservationsQuery>
{
    public GetBacktestObservationsQueryValidator(QueryLimitOptions limits)
    {
        RuleFor(query => query.After).GreaterThanOrEqualTo(0);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.BacktestObservations);
    }
}