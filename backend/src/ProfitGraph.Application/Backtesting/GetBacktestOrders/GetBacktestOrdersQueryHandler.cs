using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.GetBacktestOrders;

internal sealed class GetBacktestOrdersQueryHandler(IBacktestReader reader) : IRequestHandler<GetBacktestOrdersQuery, BacktestPageInternal<BacktestLedgerEntryInternal>>
{
    public async Task<BacktestPageInternal<BacktestLedgerEntryInternal>> Handle(GetBacktestOrdersQuery request, CancellationToken cancellationToken)
    {
        BacktestRunId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        return await reader.Orders(id, request.After, request.Limit, cancellationToken);
    }
}