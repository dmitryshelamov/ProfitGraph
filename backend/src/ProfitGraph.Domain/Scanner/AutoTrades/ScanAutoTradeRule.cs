using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     Auto trades of an owner's preset (ADR 0071, section 4): which card of each signal to take, the rule's limits and its mode. A rule
///     is created off and moves <c>Off → Paper</c> only with a proof from its preset's backtests (section 5); changing its card, its
///     limits or the preset's conditions switches it off. While on it tracks the pairs entering the preset's result as alerts do, and the
///     first check after a switch only records the result. A rule whose trades lost its daily limit pauses until the next UTC day.
/// </summary>
public sealed class ScanAutoTradeRule : AggregateRoot
{
    private readonly ScanEntryTracker _entries;

    public ScanAutoTradeRuleId Id { get; }
    public UserId OwnerId { get; }
    public ScanPresetId PresetId { get; }
    public VenueId Venue { get; }
    public ScanAutoTradeCard Card { get; private set; }
    public ScanAutoTradeLimits Limits { get; private set; }
    public ScanAutoTradeMode Mode { get; private set; }

    /// <summary>
    ///     The backtests that let the rule trade on paper, kept with their numbers; null while it is off.
    /// </summary>
    public ScanAutoTradeProof? Proof { get; private set; }

    /// <summary>
    ///     The UTC day the rule is paused for after reaching its daily loss limit; null when it is not paused.
    /// </summary>
    public DateOnly? PausedOn { get; private set; }

    /// <summary>
    ///     When the rule entered its mode: its paper trades since then are its paper result (section 7).
    /// </summary>
    public DateTimeOffset ModeSince { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CheckedAt => _entries.CheckedAt;
    public IReadOnlyDictionary<string, ScanAlertPair> Pairs => _entries.Pairs;
    public bool IsOn => Mode != ScanAutoTradeMode.Off;

    private ScanAutoTradeRule(
        ScanAutoTradeRuleId id,
        UserId ownerId,
        ScanPresetId presetId,
        VenueId venue,
        ScanAutoTradeCard card,
        ScanAutoTradeLimits limits,
        ScanAutoTradeMode mode,
        ScanAutoTradeProof? proof,
        DateOnly? pausedOn,
        ScanEntryTracker entries,
        DateTimeOffset modeSince,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        OwnerId = ownerId;
        PresetId = presetId;
        Venue = venue;
        Card = card ?? throw new ArgumentNullException(nameof(card));
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        Mode = mode;
        Proof = proof;
        PausedOn = pausedOn;
        _entries = entries;
        ModeSince = modeSince;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static ScanAutoTradeRule Create(
        UserId ownerId,
        ScanPresetId presetId,
        VenueId venue,
        ScanAutoTradeCard card,
        ScanAutoTradeLimits limits,
        DateTimeOffset now)
    {
        return new ScanAutoTradeRule(
            ScanAutoTradeRuleId.New(),
            ownerId,
            presetId,
            venue,
            card,
            limits,
            ScanAutoTradeMode.Off,
            proof: null,
            pausedOn: null,
            ScanEntryTracker.Empty(),
            now,
            now,
            now);
    }

    public static ScanAutoTradeRule Restore(
        ScanAutoTradeRuleId id,
        UserId ownerId,
        ScanPresetId presetId,
        VenueId venue,
        ScanAutoTradeCard card,
        ScanAutoTradeLimits limits,
        ScanAutoTradeMode mode,
        ScanAutoTradeProof? proof,
        DateOnly? pausedOn,
        DateTimeOffset? checkedAt,
        IReadOnlyDictionary<string, ScanAlertPair> pairs,
        DateTimeOffset modeSince,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new ScanAutoTradeRule(
            id,
            ownerId,
            presetId,
            venue,
            card,
            limits,
            mode,
            proof,
            pausedOn,
            new ScanEntryTracker(checkedAt, pairs),
            modeSince,
            createdAt,
            updatedAt);
    }

    /// <summary>
    ///     A new card or new limits switch the rule off: what proved the old card does not prove the new one.
    /// </summary>
    public void Change(ScanAutoTradeCard card, ScanAutoTradeLimits limits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(limits);

        if (card == Card && limits == Limits)
        {
            return;
        }

        Card = card;
        Limits = limits;
        SwitchOff(now);
    }

    /// <summary>
    ///     Starts paper trading with the backtests that prove the rule's card (section 5); only from off.
    /// </summary>
    public void SwitchToPaper(ScanAutoTradeProof proof, ScanAutoTradeThresholds thresholds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (Mode != ScanAutoTradeMode.Off)
        {
            throw new ScanAutoTradeRuleException(ScanAutoTradeRuleException.WrongOrder, "An auto trade rule starts paper trading only when it is off.");
        }

        if (!proof.Proves(thresholds))
        {
            throw new ScanAutoTradeRuleException(
                ScanAutoTradeRuleException.NotProven,
                "The rule's cards did not earn in each third of two backtests of opposite markets.");
        }

        Mode = ScanAutoTradeMode.Paper;
        Proof = proof;
        PausedOn = null;
        _entries.Restart();
        ModeSince = now;
        UpdatedAt = now;
    }

    public void SwitchOff(DateTimeOffset now)
    {
        Mode = ScanAutoTradeMode.Off;
        Proof = null;
        PausedOn = null;
        _entries.Restart();
        ModeSince = now;
        UpdatedAt = now;
    }

    /// <summary>
    ///     The preset's conditions changed: the rule's proof was for the old ones.
    /// </summary>
    public void ConditionsChanged(DateTimeOffset now)
    {
        if (IsOn)
        {
            SwitchOff(now);
        }
    }

    /// <summary>
    ///     Pauses the rule for the UTC <paramref name="day" /> after its trades lost its daily limit.
    /// </summary>
    public void Pause(DateOnly day, DateTimeOffset now)
    {
        if (!IsOn || PausedOn == day)
        {
            return;
        }

        PausedOn = day;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Whether the rule takes signals on the UTC <paramref name="day" />: it is on and not paused for that day.
    /// </summary>
    public bool TradesOn(DateOnly day)
    {
        return IsOn && PausedOn != day;
    }

    /// <summary>
    ///     Checks a snapshot's result while the rule is on and returns the pairs that entered it; an off rule tracks nothing.
    /// </summary>
    public IReadOnlyList<string> Check(IReadOnlyCollection<string> result, DateTimeOffset computedAt)
    {
        return IsOn ? _entries.Check(result, computedAt) : [];
    }
}