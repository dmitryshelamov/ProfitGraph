using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Converters;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.GetOptimizationRun;

internal sealed class GetOptimizationRunQueryHandler(IOptimizationReader reader, IOptimizationRunRepository optimizations, IOptimizationWindowStore windows)
    : IRequestHandler<GetOptimizationRunQuery, OptimizationRunInternal>
{
    public async Task<OptimizationRunInternal> Handle(GetOptimizationRunQuery request, CancellationToken cancellationToken)
    {
        OptimizationId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);
        OptimizationRun optimization = await optimizations.Get(id, cancellationToken)
            ?? throw new ResourceNotFoundException("The optimization does not exist.", OptimizationErrorCodes.NotFound);

        return optimization.ToInternal(await reader.State(id, cancellationToken), await windows.ReadSummary(id, cancellationToken));
    }
}