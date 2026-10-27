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

    /// <summary>
    ///     Every strategy of every owner, for work the system does on saved strategies (ADR 0038).
    /// </summary>
    Task<IReadOnlyList<Strategy>> ListAll(CancellationToken cancellationToken);

    Task<StrategyVersion?> GetVersion(StrategyId id, StrategyVersionNumber number, CancellationToken cancellationToken);

    Task<IReadOnlyList<StrategyVersion>> GetVersions(StrategyId id, CancellationToken cancellationToken);

    /// <summary>
    ///     Saves the new version only when the stored current version still equals <paramref name="expectedVersion" />.
    /// </summary>
    Task<bool> Save(Strategy strategy, StrategyVersion version, StrategyVersionNumber expectedVersion, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the name of a renamed strategy; no version is added.
    /// </summary>
    Task Rename(Strategy strategy, CancellationToken cancellationToken);
}