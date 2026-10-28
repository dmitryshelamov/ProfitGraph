using FluentValidation;

using ProfitGraph.Application.Common.Queries;

namespace ProfitGraph.Application.Optimization.GetStrategyOptimizations;

internal sealed class GetStrategyOptimizationsQueryValidator : AbstractValidator<GetStrategyOptimizationsQuery>
{
    public GetStrategyOptimizationsQueryValidator(QueryLimitOptions limits)
    {
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.StrategyOptimizations);
    }
}