using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.GetBacktestFills;

internal sealed class GetBacktestFillsQueryHandler(IBacktestReader reader) : IRequestHandler<GetBacktestFillsQuery, BacktestPageInternal<BacktestLedgerEntryInternal>>
{
    public async Task<BacktestPageInternal<BacktestLedgerEntryInternal>> Handle(GetBacktestFillsQuery request, CancellationToken cancellationToken)
    {
        BacktestRunId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        return await reader.Fills(id, request.After, request.Limit, cancellationToken);
    }
}