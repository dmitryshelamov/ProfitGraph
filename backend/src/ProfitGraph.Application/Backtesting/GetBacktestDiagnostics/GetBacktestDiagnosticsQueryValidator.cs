using FluentValidation;

using ProfitGraph.Application.Common.Queries;

namespace ProfitGraph.Application.Backtesting.GetBacktestDiagnostics;

internal sealed class GetBacktestDiagnosticsQueryValidator : AbstractValidator<GetBacktestDiagnosticsQuery>
{
    public GetBacktestDiagnosticsQueryValidator(QueryLimitOptions limits)
    {
        RuleFor(query => query.After).GreaterThanOrEqualTo(0);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.BacktestDiagnostics);
    }
}