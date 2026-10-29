using MediatR;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Optimization.GetOptimizations;

internal sealed class GetOptimizationsQueryHandler(IOptimizationReader reader) : IRequestHandler<GetOptimizationsQuery, OptimizationSummaryPageInternal>
{
    public Task<OptimizationSummaryPageInternal> Handle(GetOptimizationsQuery request, CancellationToken cancellationToken)
    {
        return reader.List(new UserId(request.UserId), request.Filter, request.After, request.Limit, cancellationToken);
    }
}