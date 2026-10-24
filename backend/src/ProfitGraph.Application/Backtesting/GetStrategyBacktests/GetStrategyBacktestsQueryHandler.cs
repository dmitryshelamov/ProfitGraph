using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Application.Strategies;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Backtesting.GetStrategyBacktests;

internal sealed class GetStrategyBacktestsQueryHandler(IStrategyRepository strategies, IBacktestReader reader)
    : IRequestHandler<GetStrategyBacktestsQuery, BacktestSummaryPageInternal>
{
    public async Task<BacktestSummaryPageInternal> Handle(GetStrategyBacktestsQuery request, CancellationToken cancellationToken)
    {
        Strategy strategy = await strategies.GetOwned(request.StrategyId, request.UserId, cancellationToken);

        return await reader.ForStrategy(strategy.Id, request.After, request.Limit, cancellationToken);
    }
}