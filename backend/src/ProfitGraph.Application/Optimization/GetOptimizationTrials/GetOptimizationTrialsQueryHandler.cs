using MediatR;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.GetOptimizationTrials;

internal sealed class GetOptimizationTrialsQueryHandler(IOptimizationReader reader) : IRequestHandler<GetOptimizationTrialsQuery, OptimizationTrialPageInternal>
{
    public async Task<OptimizationTrialPageInternal> Handle(GetOptimizationTrialsQuery request, CancellationToken cancellationToken)
    {
        OptimizationId id = await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        return await reader.Trials(id, request.After, request.Limit, cancellationToken);
    }
}