using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     A user's strategy. Saving a definition creates a new immutable version; version numbers are never reused.
/// </summary>
public sealed class Strategy : AggregateRoot
{
    public StrategyId Id { get; }
    public UserId OwnerId { get; }
    public StrategyName Name { get; private set; }
    public StrategyVersionNumber CurrentVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Strategy(
        StrategyId id,
        UserId ownerId,
        StrategyName name,
        StrategyVersionNumber currentVersion,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        OwnerId = ownerId;
        Name = name;
        CurrentVersion = currentVersion;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    ///     Creates a strategy owned by the given user together with its first version.
    /// </summary>
    public static (Strategy Strategy, StrategyVersion Version) Create(
        StrategyId id,
        UserId ownerId,
        StrategyName name,
        StrategyDefinition definition,
        GraphLayout layout,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        var strategy = new Strategy(id, ownerId, name, StrategyVersionNumber.First, now, now);

        return (strategy, new StrategyVersion(StrategyVersionId.New(), id, StrategyVersionNumber.First, definition, layout, now));
    }

    public static Strategy Restore(
        StrategyId id,
        UserId ownerId,
        StrategyName name,
        StrategyVersionNumber currentVersion,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new Strategy(id, ownerId, name, currentVersion, createdAt, updatedAt);
    }

    public bool IsOwnedBy(UserId userId)
    {
        return OwnerId == userId;
    }

    /// <summary>
    ///     Renames the strategy and records the definition as the next version.
    /// </summary>
    public StrategyVersion Save(StrategyName name, StrategyDefinition definition, GraphLayout layout, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        CurrentVersion = CurrentVersion.Next();
        UpdatedAt = now;

        return new StrategyVersion(StrategyVersionId.New(), Id, CurrentVersion, definition, layout, now);
    }

    /// <summary>
    ///     Renames the strategy without a new version, so running bots keep their version (ADR 0019). Returns whether the name changed.
    /// </summary>
    public bool Rename(StrategyName name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name == Name)
        {
            return false;
        }

        Name = name;
        UpdatedAt = now;

        return true;
    }
}