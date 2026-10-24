using MediatR;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.GetOptimizationCursor;

internal sealed class GetOptimizationCursorQueryHandler(IOptimizationReader reader) : IRequestHandler<GetOptimizationCursorQuery, OptimizationCursorInternal>
{
    public async Task<OptimizationCursorInternal> Handle(GetOptimizationCursorQuery request, CancellationToken cancellationToken)
    {
        OptimizationId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        return await reader.Cursor(id, cancellationToken);
    }
}