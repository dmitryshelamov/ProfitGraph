using FluentValidation;

namespace ProfitGraph.Application.Optimization.GetOptimizationTrials;

internal sealed class GetOptimizationTrialsQueryValidator : AbstractValidator<GetOptimizationTrialsQuery>
{
    public GetOptimizationTrialsQueryValidator()
    {
        RuleFor(query => query.After).GreaterThanOrEqualTo(0);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, GetOptimizationTrialsQuery.MaxLimit);
    }
}