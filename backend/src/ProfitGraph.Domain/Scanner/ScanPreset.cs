using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     A user's named filter of one venue's scanner table (ADR 0059, section 4): conditions on metrics, a sort, and whether pairs
///     without recent trades are hidden. Each metric appears in at most one condition.
/// </summary>
public sealed class ScanPreset : AggregateRoot
{
    public const int MaxConditions = 20;

    public ScanPresetId Id { get; }
    public UserId OwnerId { get; }
    public VenueId Venue { get; }
    public ScanPresetName Name { get; private set; }
    public IReadOnlyList<ScanCondition> Conditions { get; private set; }
    public ScanSort Sort { get; private set; }
    public bool HideStale { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScanPreset(
        ScanPresetId id,
        UserId ownerId,
        VenueId venue,
        ScanPresetName name,
        IReadOnlyList<ScanCondition> conditions,
        ScanSort sort,
        bool hideStale,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        OwnerId = ownerId;
        Venue = venue;
        Name = name;
        Conditions = conditions;
        Sort = sort;
        HideStale = hideStale;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static ScanPreset Create(
        UserId ownerId,
        VenueId venue,
        ScanPresetName name,
        IReadOnlyList<ScanCondition> conditions,
        ScanSort sort,
        bool hideStale,
        DateTimeOffset now)
    {
        return new ScanPreset(ScanPresetId.New(), ownerId, venue, name, Checked(conditions), sort, hideStale, now, now);
    }

    public static ScanPreset Restore(
        ScanPresetId id,
        UserId ownerId,
        VenueId venue,
        ScanPresetName name,
        IReadOnlyList<ScanCondition> conditions,
        ScanSort sort,
        bool hideStale,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new ScanPreset(id, ownerId, venue, name, conditions, sort, hideStale, createdAt, updatedAt);
    }

    /// <summary>
    ///     Replaces the name, the conditions, the sort and the stale switch; returns whether anything changed.
    /// </summary>
    public bool Change(ScanPresetName name, IReadOnlyList<ScanCondition> conditions, ScanSort sort, bool hideStale, DateTimeOffset now)
    {
        IReadOnlyList<ScanCondition> checkedConditions = Checked(conditions);

        if (Name == name && Conditions.SequenceEqual(checkedConditions) && Sort == sort && HideStale == hideStale)
        {
            return false;
        }

        Name = name;
        Conditions = checkedConditions;
        Sort = sort;
        HideStale = hideStale;
        UpdatedAt = now;

        return true;
    }

    private static IReadOnlyList<ScanCondition> Checked(IReadOnlyList<ScanCondition> conditions)
    {
        if (conditions.Count > MaxConditions || conditions.DistinctBy(condition => condition.Metric).Count() != conditions.Count)
        {
            throw new ScanPresetRuleException(
                ScanPresetRuleException.InvalidCondition,
                $"A scanner preset has at most {MaxConditions} conditions, one per metric.");
        }

        return [.. conditions];
    }
}