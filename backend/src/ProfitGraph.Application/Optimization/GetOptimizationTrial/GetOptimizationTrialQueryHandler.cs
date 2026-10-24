using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.GetOptimizationTrial;

internal sealed class GetOptimizationTrialQueryHandler(IOptimizationReader reader) : IRequestHandler<GetOptimizationTrialQuery, OptimizationTrialInternal>
{
    public async Task<OptimizationTrialInternal> Handle(GetOptimizationTrialQuery request, CancellationToken cancellationToken)
    {
        OptimizationId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        return await reader.Trial(id, request.WindowIndex, request.TrialIndex, cancellationToken)
            ?? throw new ResourceNotFoundException("The trial does not exist.", "optimization_trial_not_found");
    }
}