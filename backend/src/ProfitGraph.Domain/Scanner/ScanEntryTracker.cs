namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     Which pairs entered a preset's result (ADR 0059, section 5): each snapshot is checked once, a pair counts when it enters the
///     result, at most once per <see cref="ScanAlertWatch.Cooldown" />, and the first check only records the result, so starting to
///     track never reports the pairs already in it. The tracker keeps the pairs in the result and the reported pairs whose cooldown
///     still runs. Alert watches and auto trade rules (ADR 0071, section 6) track the same way.
/// </summary>
public sealed class ScanEntryTracker
{
    private Dictionary<string, ScanAlertPair> _pairs;

    /// <summary>
    ///     The computation time of the last snapshot checked; null before the first check.
    /// </summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    public IReadOnlyDictionary<string, ScanAlertPair> Pairs => _pairs;

    public ScanEntryTracker(DateTimeOffset? checkedAt, IReadOnlyDictionary<string, ScanAlertPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        CheckedAt = checkedAt;
        _pairs = new Dictionary<string, ScanAlertPair>(pairs, StringComparer.Ordinal);
    }

    public static ScanEntryTracker Empty()
    {
        return new ScanEntryTracker(checkedAt: null, new Dictionary<string, ScanAlertPair>());
    }

    /// <summary>
    ///     Checks the result of the snapshot computed at <paramref name="computedAt" /> and returns the pairs that entered it, in ordinal
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
    ///     The next check only records the result again; the cooldowns of reported pairs stay.
    /// </summary>
    public void Restart()
    {
        CheckedAt = null;
    }

    private static bool Cooled(ScanAlertPair? pair, DateTimeOffset now)
    {
        return pair?.AlertedAt is not { } alertedAt || now - alertedAt >= ScanAlertWatch.Cooldown;
    }
}