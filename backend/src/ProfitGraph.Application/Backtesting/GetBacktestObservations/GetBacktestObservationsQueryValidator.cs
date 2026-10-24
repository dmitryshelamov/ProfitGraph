using FluentValidation;

namespace ProfitGraph.Application.Backtesting.GetBacktestObservations;

internal sealed class GetBacktestObservationsQueryValidator : AbstractValidator<GetBacktestObservationsQuery>
{
    public GetBacktestObservationsQueryValidator()
    {
        RuleFor(query => query.After).GreaterThanOrEqualTo(0);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, GetBacktestObservationsQuery.MaxLimit);
    }
}