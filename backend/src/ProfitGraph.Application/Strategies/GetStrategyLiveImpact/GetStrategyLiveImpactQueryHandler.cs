using MediatR;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.GetStrategyLiveImpact;

internal sealed class GetStrategyLiveImpactQueryHandler(IStrategyRepository strategies, IBotRepository bots)
    : IRequestHandler<GetStrategyLiveImpactQuery, IReadOnlyList<LiveImpactBotInternal>>
{
    public async Task<IReadOnlyList<LiveImpactBotInternal>> Handle(GetStrategyLiveImpactQuery query, CancellationToken cancellationToken)
    {
        Strategy strategy = await strategies.GetOwned(query.Id, query.UserId, cancellationToken);

        return LiveImpact.Of(await bots.ListDesiredRunningOnStrategy(strategy.Id, cancellationToken));
    }
}