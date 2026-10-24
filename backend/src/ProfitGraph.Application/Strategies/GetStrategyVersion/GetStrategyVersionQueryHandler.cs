using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.GetStrategyVersion;

internal sealed class GetStrategyVersionQueryHandler(IStrategyRepository repository) : IRequestHandler<GetStrategyVersionQuery, StrategyVersionInternal>
{
    public async Task<StrategyVersionInternal> Handle(GetStrategyVersionQuery request, CancellationToken cancellationToken)
    {
        Strategy strategy = await repository.GetOwned(request.Id, request.UserId, cancellationToken);
        StrategyVersion version = await repository.GetVersion(strategy.Id, new StrategyVersionNumber(request.Version), cancellationToken)
            ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");

        return version.ToInternal();
    }
}