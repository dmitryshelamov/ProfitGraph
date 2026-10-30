using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     A user's named filter of one venue's scanner table (ADR 0059, section 4): conditions on metrics, a sort, and whether pairs
///     without recent trades are hidden. Each metric appears in at most one condition. Its trade direction says which trade cards its
///     pairs offer (ADR 0069).
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
    public ScanTradeDirection TradeDirection { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public ScanFilter Filter => new ScanFilter(Conditions, HideStale);

    private ScanPreset(
        ScanPresetId id,
        UserId ownerId,
        VenueId venue,
        ScanPresetName name,
        IReadOnlyList<ScanCondition> conditions,
        ScanSort sort,
        bool hideStale,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        ScanTradeDirection tradeDirection)
    {
        if (!Enum.IsDefined(tradeDirection))
        {
            throw new ScanPresetRuleException(ScanPresetRuleException.InvalidCondition, "A scanner preset trades long, short or both ways.");
        }

        Id = id;
        OwnerId = ownerId;
        Venue = venue;
        Name = name;
        Conditions = conditions;
        Sort = sort;
        HideStale = hideStale;
        TradeDirection = tradeDirection;
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
        DateTimeOffset now,
        ScanTradeDirection tradeDirection = ScanTradeDirection.Both)
    {
        return new ScanPreset(ScanPresetId.New(), ownerId, venue, name, Checked(conditions), sort, hideStale, now, now, tradeDirection);
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
        DateTimeOffset updatedAt,
        ScanTradeDirection tradeDirection = ScanTradeDirection.Both)
    {
        return new ScanPreset(id, ownerId, venue, name, conditions, sort, hideStale, createdAt, updatedAt, tradeDirection);
    }

    /// <summary>
    ///     Replaces the name, the conditions, the sort, the stale switch and the trade direction; returns whether anything changed.
    /// </summary>
    public bool Change(
        ScanPresetName name,
        IReadOnlyList<ScanCondition> conditions,
        ScanSort sort,
        bool hideStale,
        ScanTradeDirection tradeDirection,
        DateTimeOffset now)
    {
        IReadOnlyList<ScanCondition> checkedConditions = Checked(conditions);

        if (!Enum.IsDefined(tradeDirection))
        {
            throw new ScanPresetRuleException(ScanPresetRuleException.InvalidCondition, "A scanner preset trades long, short or both ways.");
        }

        if (Name == name && Conditions.SequenceEqual(checkedConditions) && Sort == sort && HideStale == hideStale && TradeDirection == tradeDirection)
        {
            return false;
        }

        Name = name;
        Conditions = checkedConditions;
        Sort = sort;
        HideStale = hideStale;
        TradeDirection = tradeDirection;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    ///     Whether the preset keeps a pair, as the page filters it: every condition holds and, when asked, the pair has traded in the
    ///     last 15 minutes.
    /// </summary>
    public bool Keeps(ScanMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return Filter.Keeps(metrics);
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