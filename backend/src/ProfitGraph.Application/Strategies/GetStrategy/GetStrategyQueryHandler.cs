using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.GetStrategy;

internal sealed class GetStrategyQueryHandler(IStrategyRepository repository) : IRequestHandler<GetStrategyQuery, StrategyInternal>
{
    public async Task<StrategyInternal> Handle(GetStrategyQuery request, CancellationToken cancellationToken)
    {
        Strategy strategy = await repository.GetOwned(request.Id, request.UserId, cancellationToken);
        StrategyVersion version = await repository.GetVersion(request.Id, strategy.CurrentVersion, cancellationToken)
            ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");

        return strategy.ToInternal(version);
    }
}