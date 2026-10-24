using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.GetBacktestCursor;

internal sealed class GetBacktestCursorQueryHandler(IBacktestReader reader) : IRequestHandler<GetBacktestCursorQuery, BacktestCursorInternal>
{
    public async Task<BacktestCursorInternal> Handle(GetBacktestCursorQuery request, CancellationToken cancellationToken)
    {
        BacktestRunId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);
        BacktestRunStateInternal state = await reader.State(id, cancellationToken);

        return new BacktestCursorInternal(state.LastSequence, state.Outcome);
    }
}