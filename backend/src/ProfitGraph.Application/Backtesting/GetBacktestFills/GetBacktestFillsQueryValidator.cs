using FluentValidation;

namespace ProfitGraph.Application.Backtesting.GetBacktestFills;

internal sealed class GetBacktestFillsQueryValidator : AbstractValidator<GetBacktestFillsQuery>
{
    public GetBacktestFillsQueryValidator()
    {
        RuleFor(query => query.After).GreaterThanOrEqualTo(0);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, GetBacktestFillsQuery.MaxLimit);
    }
}