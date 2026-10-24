using MediatR;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.GetStrategies;

internal sealed class GetStrategiesQueryHandler(IStrategyRepository repository) : IRequestHandler<GetStrategiesQuery, IReadOnlyList<StrategySummaryInternal>>
{
    private const int Limit = 500;

    public async Task<IReadOnlyList<StrategySummaryInternal>> Handle(GetStrategiesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<StrategySummary> strategies = await repository.List(request.UserId, Limit, cancellationToken);

        return
        [
            .. strategies.Select(strategy => new StrategySummaryInternal(strategy.Id, strategy.Name, strategy.CurrentVersion, strategy.UpdatedAt, strategy.TemplateKind))
        ];
    }
}