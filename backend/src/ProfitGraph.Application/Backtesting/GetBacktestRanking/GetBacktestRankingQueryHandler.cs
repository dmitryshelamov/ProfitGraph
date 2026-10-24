using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Backtesting.GetBacktestRanking;

internal sealed class GetBacktestRankingQueryHandler(IBacktestReader reader) : IRequestHandler<GetBacktestRankingQuery, BacktestSummaryPageInternal>
{
    public Task<BacktestSummaryPageInternal> Handle(GetBacktestRankingQuery request, CancellationToken cancellationToken)
    {
        return reader.Ranking(
            new UserId(request.UserId),
            request.Quote,
            request.Symbol,
            request.Timeframe,
            request.After,
            request.Limit,
            request.Sort,
            request.Ascending,
            cancellationToken);
    }
}