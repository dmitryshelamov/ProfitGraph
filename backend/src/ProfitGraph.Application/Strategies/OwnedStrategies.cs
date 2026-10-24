using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies;

/// <summary>
///     Loads a strategy for its owner. A strategy of another user is reported as missing, so its existence is not revealed.
/// </summary>
internal static class OwnedStrategies
{
    private const string NotFoundCode = "strategy_not_found";

    /// <summary>
    ///     Takes the identifiers as they arrive in a request; an empty identifier cannot name a strategy and is reported as missing too.
    /// </summary>
    internal static async Task<Strategy> GetOwned(this IStrategyRepository repository, Guid id, Guid userId, CancellationToken cancellationToken)
    {
        Strategy? strategy = id == Guid.Empty ? null : await repository.Get(new StrategyId(id), cancellationToken);

        return strategy is not null && strategy.IsOwnedBy(new UserId(userId))
            ? strategy
            : throw new ResourceNotFoundException("The strategy does not exist.", NotFoundCode);
    }
}