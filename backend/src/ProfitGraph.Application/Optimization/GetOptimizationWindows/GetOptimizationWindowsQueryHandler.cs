using MediatR;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.GetOptimizationWindows;

internal sealed class GetOptimizationWindowsQueryHandler(IOptimizationReader reader, IOptimizationWindowStore windows)
    : IRequestHandler<GetOptimizationWindowsQuery, OptimizationWindowsInternal>
{
    public async Task<OptimizationWindowsInternal> Handle(GetOptimizationWindowsQuery request, CancellationToken cancellationToken)
    {
        OptimizationId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        return new OptimizationWindowsInternal(await windows.ReadWindows(id, cancellationToken));
    }
}