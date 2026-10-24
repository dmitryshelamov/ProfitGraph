using FluentValidation;

namespace ProfitGraph.Application.Backtesting.GetBacktestDiagnostics;

internal sealed class GetBacktestDiagnosticsQueryValidator : AbstractValidator<GetBacktestDiagnosticsQuery>
{
    public GetBacktestDiagnosticsQueryValidator()
    {
        RuleFor(query => query.After).GreaterThanOrEqualTo(0);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, GetBacktestDiagnosticsQuery.MaxLimit);
    }
}