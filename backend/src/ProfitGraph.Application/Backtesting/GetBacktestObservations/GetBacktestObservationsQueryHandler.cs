using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.GetBacktestObservations;

internal sealed class GetBacktestObservationsQueryHandler(IBacktestReader reader)
    : IRequestHandler<GetBacktestObservationsQuery, BacktestPageInternal<BacktestObservationInternal>>
{
    public async Task<BacktestPageInternal<BacktestObservationInternal>> Handle(GetBacktestObservationsQuery request, CancellationToken cancellationToken)
    {
        BacktestRunId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        return await reader.Observations(id, request.After, request.Limit, cancellationToken);
    }
}