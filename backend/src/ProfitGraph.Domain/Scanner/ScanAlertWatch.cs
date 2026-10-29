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

    private Dictionary<string, ScanAlertPair> _pairs;

    public ScanPresetId PresetId { get; }
    public UserId OwnerId { get; }
    public DateTimeOffset EnabledAt { get; }

    /// <summary>
    ///     The computation time of the last snapshot checked; null before the first check.
    /// </summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    public IReadOnlyDictionary<string, ScanAlertPair> Pairs => _pairs;

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
        CheckedAt = checkedAt;
        _pairs = new Dictionary<string, ScanAlertPair>(pairs, StringComparer.Ordinal);
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
        ArgumentNullException.ThrowIfNull(result);

        if (CheckedAt is { } last && computedAt <= last)
        {
            return [];
        }

        bool recordOnly = CheckedAt is null;
        var next = new Dictionary<string, ScanAlertPair>(StringComparer.Ordinal);
        List<string> entered = [];

        foreach (string symbol in result.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            ScanAlertPair? known = _pairs.GetValueOrDefault(symbol);

            if (known is { InResult: true })
            {
                next[symbol] = known;
            }
            else if (!recordOnly && Cooled(known, computedAt))
            {
                entered.Add(symbol);
                next[symbol] = new ScanAlertPair(InResult: true, computedAt);
            }
            else
            {
                next[symbol] = new ScanAlertPair(InResult: true, known?.AlertedAt);
            }
        }

        foreach ((string symbol, ScanAlertPair pair) in _pairs)
        {
            if (!next.ContainsKey(symbol) && !Cooled(pair, computedAt))
            {
                next[symbol] = pair with { InResult = false };
            }
        }

        _pairs = next;
        CheckedAt = computedAt;

        return entered;
    }

    /// <summary>
    ///     The preset's conditions changed: the next check only records the result again, and the cooldowns of reported pairs stay.
    /// </summary>
    public void Restart()
    {
        CheckedAt = null;
    }

    private static bool Cooled(ScanAlertPair? pair, DateTimeOffset now)
    {
        return pair?.AlertedAt is not { } alertedAt || now - alertedAt >= Cooldown;
    }
}