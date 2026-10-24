using FluentValidation;

namespace ProfitGraph.Application.Optimization.GetStrategyOptimizations;

internal sealed class GetStrategyOptimizationsQueryValidator : AbstractValidator<GetStrategyOptimizationsQuery>
{
    public GetStrategyOptimizationsQueryValidator()
    {
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, GetStrategyOptimizationsQuery.MaxLimit);
    }
}