using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Backtesting.GetBacktestFacets;

internal sealed class GetBacktestFacetsQueryHandler(IBacktestReader reader) : IRequestHandler<GetBacktestFacetsQuery, BacktestFacetsInternal>
{
    public Task<BacktestFacetsInternal> Handle(GetBacktestFacetsQuery request, CancellationToken cancellationToken)
    {
        return reader.Facets(new UserId(request.UserId), cancellationToken);
    }
}