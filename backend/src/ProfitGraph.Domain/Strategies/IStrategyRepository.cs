using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     Stores strategies and their immutable versions. Writes run in the caller's transaction.
/// </summary>
public interface IStrategyRepository
{
    Task Add(Strategy strategy, StrategyVersion version, CancellationToken cancellationToken);

    Task<Strategy?> Get(StrategyId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StrategySummary>> List(UserId ownerId, int limit, CancellationToken cancellationToken);

    Task<StrategyVersion?> GetVersion(StrategyId id, StrategyVersionNumber number, CancellationToken cancellationToken);

    Task<IReadOnlyList<StrategyVersion>> GetVersions(StrategyId id, CancellationToken cancellationToken);

    /// <summary>
    ///     Saves the new version only when the stored current version still equals <paramref name="expectedVersion" />.
    /// </summary>
    Task<bool> Save(Strategy strategy, StrategyVersion version, StrategyVersionNumber expectedVersion, CancellationToken cancellationToken);
}