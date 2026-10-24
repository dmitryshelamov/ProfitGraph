using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.GetBacktestChart;

internal sealed class GetBacktestChartQueryHandler(IBacktestReader reader) : IRequestHandler<GetBacktestChartQuery, BacktestChartInternal>
{
    public async Task<BacktestChartInternal> Handle(GetBacktestChartQuery request, CancellationToken cancellationToken)
    {
        BacktestRunId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        return await reader.Chart(id, BacktestChart.MaxPoints, cancellationToken);
    }
}