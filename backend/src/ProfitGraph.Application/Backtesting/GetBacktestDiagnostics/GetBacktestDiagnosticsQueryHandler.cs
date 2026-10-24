using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.GetBacktestDiagnostics;

internal sealed class GetBacktestDiagnosticsQueryHandler(IBacktestReader reader)
    : IRequestHandler<GetBacktestDiagnosticsQuery, BacktestPageInternal<BacktestDiagnosticInternal>>
{
    public async Task<BacktestPageInternal<BacktestDiagnosticInternal>> Handle(GetBacktestDiagnosticsQuery request, CancellationToken cancellationToken)
    {
        BacktestRunId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        return await reader.Diagnostics(id, request.After, request.Limit, cancellationToken);
    }
}