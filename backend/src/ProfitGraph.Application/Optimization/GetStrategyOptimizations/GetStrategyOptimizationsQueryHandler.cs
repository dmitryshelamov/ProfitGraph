using MediatR;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Application.Strategies;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Optimization.GetStrategyOptimizations;

internal sealed class GetStrategyOptimizationsQueryHandler(IStrategyRepository strategies, IOptimizationReader reader)
    : IRequestHandler<GetStrategyOptimizationsQuery, OptimizationSummaryPageInternal>
{
    public async Task<OptimizationSummaryPageInternal> Handle(GetStrategyOptimizationsQuery request, CancellationToken cancellationToken)
    {
        Strategy strategy = await strategies.GetOwned(request.StrategyId, request.UserId, cancellationToken);

        return await reader.ForStrategy(strategy.Id, request.After, request.Limit, cancellationToken);
    }
}