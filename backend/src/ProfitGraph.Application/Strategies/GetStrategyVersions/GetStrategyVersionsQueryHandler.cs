using MediatR;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Ports;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.GetStrategyVersions;

internal sealed class GetStrategyVersionsQueryHandler(IStrategyRepository repository, IStrategyVersionReader versions)
    : IRequestHandler<GetStrategyVersionsQuery, IReadOnlyList<StrategyVersionSummaryInternal>>
{
    public async Task<IReadOnlyList<StrategyVersionSummaryInternal>> Handle(GetStrategyVersionsQuery request, CancellationToken cancellationToken)
    {
        await repository.GetOwned(request.Id, request.UserId, cancellationToken);

        return await versions.List(request.Id, cancellationToken);
    }
}