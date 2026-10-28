using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Application.Common.Queries;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.GetStrategies;

internal sealed class GetStrategiesQueryHandler(IStrategyRepository repository, IBacktestReader backtests, QueryLimitOptions limits)
    : IRequestHandler<GetStrategiesQuery, IReadOnlyList<StrategySummaryInternal>>
{
    public async Task<IReadOnlyList<StrategySummaryInternal>> Handle(GetStrategiesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<StrategySummary> strategies = await repository.List(new UserId(request.UserId), limits.Strategies, cancellationToken);
        IReadOnlyDictionary<Guid, BacktestSummaryInternal> latest = await backtests.Latest([.. strategies.Select(strategy => strategy.Id.Value)], cancellationToken);

        return
        [
            .. strategies.Select(strategy => new StrategySummaryInternal(
                strategy.Id.Value,
                strategy.Name.Value,
                strategy.CurrentVersion.Value,
                strategy.UpdatedAt,
                strategy.TemplateKind,
                latest.GetValueOrDefault(strategy.Id.Value)))
        ];
    }
}