using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.GetScanBacktest;

internal sealed class GetScanBacktestQueryHandler(IScanBacktestRepository backtests) : IRequestHandler<GetScanBacktestQuery, ScanBacktestInternal>
{
    public async Task<ScanBacktestInternal> Handle(GetScanBacktestQuery query, CancellationToken cancellationToken)
    {
        ScanBacktest backtest = await backtests.Get(new ScanBacktestId(query.BacktestId), new UserId(query.UserId), cancellationToken) ??
            throw new ResourceNotFoundException("The scanner backtest does not exist.", ScanBacktestErrorCodes.NotFound);

        return backtest.ToInternal();
    }
}