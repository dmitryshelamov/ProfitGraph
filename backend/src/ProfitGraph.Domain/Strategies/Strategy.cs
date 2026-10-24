using ProfitGraph.Domain.Common;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     A user's strategy. Saving a definition creates a new immutable version; version numbers are never reused.
/// </summary>
public sealed class Strategy : AggregateRoot
{
    public const int MaxNameLength = 200;

    public Guid Id { get; }
    public Guid OwnerId { get; }
    public string Name { get; private set; }
    public int CurrentVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Strategy(Guid id, Guid ownerId, string name, int currentVersion, DateTimeOffset createdAt, DateTimeOffset updatedAt)
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
        Guid id,
        Guid ownerId,
        string name,
        StrategyDefinition definition,
        GraphLayout layout,
        DateTimeOffset now)
    {
        if (id == Guid.Empty || ownerId == Guid.Empty)
        {
            throw new ArgumentException("A strategy needs an identifier and an owner.");
        }

        var strategy = new Strategy(id, ownerId, ValidName(name), currentVersion: 1, now, now);

        return (strategy, new StrategyVersion(Guid.NewGuid(), id, Number: 1, definition, layout, now));
    }

    public static Strategy Restore(Guid id, Guid ownerId, string name, int currentVersion, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty || ownerId == Guid.Empty || currentVersion < 1)
        {
            throw new ArgumentException("Invalid stored strategy.");
        }

        return new Strategy(id, ownerId, ValidName(name), currentVersion, createdAt, updatedAt);
    }

    public bool IsOwnedBy(Guid userId)
    {
        return OwnerId == userId;
    }

    /// <summary>
    ///     Renames the strategy and records the definition as the next version.
    /// </summary>
    public StrategyVersion Save(string name, StrategyDefinition definition, GraphLayout layout, DateTimeOffset now)
    {
        Name = ValidName(name);
        CurrentVersion = checked(CurrentVersion + 1);
        UpdatedAt = now;

        return new StrategyVersion(Guid.NewGuid(), Id, CurrentVersion, definition, layout, now);
    }

    private static string ValidName(string? name)
    {
        string trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length is 0 or > MaxNameLength)
        {
            throw new StrategyRuleException(StrategyRuleException.InvalidName, $"A strategy name must contain 1 to {MaxNameLength} characters.");
        }

        return trimmed;
    }
}