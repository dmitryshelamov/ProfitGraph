using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Converters;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.GetBacktestRun;

internal sealed class GetBacktestRunQueryHandler(IBacktestReader reader, IBacktestRunRepository runs) : IRequestHandler<GetBacktestRunQuery, BacktestRunInternal>
{
    public async Task<BacktestRunInternal> Handle(GetBacktestRunQuery request, CancellationToken cancellationToken)
    {
        BacktestRunId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);
        BacktestRun run = await runs.Get(id, cancellationToken)
            ?? throw new ResourceNotFoundException("The backtest run does not exist.", BacktestErrorCodes.RunNotFound);

        return run.ToInternal(await reader.State(id, cancellationToken));
    }
}