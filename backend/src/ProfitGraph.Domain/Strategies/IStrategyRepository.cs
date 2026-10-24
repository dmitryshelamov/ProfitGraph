namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     Stores strategies and their immutable versions. Writes run in the caller's transaction.
/// </summary>
public interface IStrategyRepository
{
    Task Add(Strategy strategy, StrategyVersion version, CancellationToken cancellationToken);

    Task<Strategy?> Get(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StrategySummary>> List(Guid ownerId, int limit, CancellationToken cancellationToken);

    Task<StrategyVersion?> GetVersion(Guid id, int number, CancellationToken cancellationToken);

    Task<IReadOnlyList<StrategyVersion>> GetVersions(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     Saves the new version only when the stored current version still equals <paramref name="expectedVersion" />.
    /// </summary>
    Task<bool> Save(Strategy strategy, StrategyVersion version, int expectedVersion, CancellationToken cancellationToken);
}