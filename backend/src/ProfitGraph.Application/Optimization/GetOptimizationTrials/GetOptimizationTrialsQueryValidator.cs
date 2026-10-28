using FluentValidation;

using ProfitGraph.Application.Common.Queries;

namespace ProfitGraph.Application.Optimization.GetOptimizationTrials;

internal sealed class GetOptimizationTrialsQueryValidator : AbstractValidator<GetOptimizationTrialsQuery>
{
    public GetOptimizationTrialsQueryValidator(QueryLimitOptions limits)
    {
        RuleFor(query => query.After).GreaterThanOrEqualTo(0);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.OptimizationTrials);
    }
}