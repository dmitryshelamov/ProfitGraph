using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     A preset whose alerts are on (ADR 0059, section 5); a preset without a watch sends none. Each snapshot is checked once: a pair
///     is reported when it enters the preset's result, at most once per <see cref="Cooldown" />. The first check after the alerts are
///     switched on only records the result, so switching them on never reports the pairs already in it. The watch keeps the pairs in
///     the result and the reported pairs whose cooldown still runs.
/// </summary>
public sealed class ScanAlertWatch : AggregateRoot
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);

    private readonly ScanEntryTracker _entries;

    public ScanPresetId PresetId { get; }
    public UserId OwnerId { get; }
    public DateTimeOffset EnabledAt { get; }

    /// <summary>
    ///     The computation time of the last snapshot checked; null before the first check.
    /// </summary>
    public DateTimeOffset? CheckedAt => _entries.CheckedAt;

    public IReadOnlyDictionary<string, ScanAlertPair> Pairs => _entries.Pairs;

    private ScanAlertWatch(
        ScanPresetId presetId,
        UserId ownerId,
        DateTimeOffset enabledAt,
        DateTimeOffset? checkedAt,
        IReadOnlyDictionary<string, ScanAlertPair> pairs)
    {
        PresetId = presetId;
        OwnerId = ownerId;
        EnabledAt = enabledAt;
        _entries = new ScanEntryTracker(checkedAt, pairs);
    }

    public static ScanAlertWatch Start(ScanPresetId presetId, UserId ownerId, DateTimeOffset now)
    {
        return new ScanAlertWatch(presetId, ownerId, now, checkedAt: null, new Dictionary<string, ScanAlertPair>());
    }

    public static ScanAlertWatch Restore(
        ScanPresetId presetId,
        UserId ownerId,
        DateTimeOffset enabledAt,
        DateTimeOffset? checkedAt,
        IReadOnlyDictionary<string, ScanAlertPair> pairs)
    {
        return new ScanAlertWatch(presetId, ownerId, enabledAt, checkedAt, pairs);
    }

    /// <summary>
    ///     Checks the result of the snapshot computed at <paramref name="computedAt" /> and returns the pairs to report, in ordinal
    ///     order. A snapshot not newer than the last one checked changes nothing.
    /// </summary>
    public IReadOnlyList<string> Check(IReadOnlyCollection<string> result, DateTimeOffset computedAt)
    {
        return _entries.Check(result, computedAt);
    }

    /// <summary>
    ///     The preset's conditions changed: the next check only records the result again, and the cooldowns of reported pairs stay.
    /// </summary>
    public void Restart()
    {
        _entries.Restart();
    }
}