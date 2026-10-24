using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies;

/// <summary>
///     Loads a strategy for its owner. A strategy of another user is reported as missing, so its existence is not revealed.
/// </summary>
internal static class OwnedStrategies
{
    private const string NotFoundCode = "strategy_not_found";

    internal static async Task<Strategy> GetOwned(this IStrategyRepository repository, Guid id, Guid userId, CancellationToken cancellationToken)
    {
        Strategy? strategy = await repository.Get(id, cancellationToken);

        return strategy is not null && strategy.IsOwnedBy(userId) ? strategy : throw new ResourceNotFoundException("The strategy does not exist.", NotFoundCode);
    }
}