using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization;

/// <summary>
///     An optimization of another user is reported as missing, so its existence is not revealed.
/// </summary>
internal static class OwnedOptimizations
{
    internal static async Task<OptimizationId> EnsureOwned(this IOptimizationReader reader, Guid id, Guid userId, CancellationToken cancellationToken)
    {
        if (id != Guid.Empty)
        {
            var optimizationId = new OptimizationId(id);

            if (await reader.Owner(optimizationId, cancellationToken) == new UserId(userId))
            {
                return optimizationId;
            }
        }

        throw new ResourceNotFoundException("The optimization does not exist.", OptimizationErrorCodes.NotFound);
    }
}