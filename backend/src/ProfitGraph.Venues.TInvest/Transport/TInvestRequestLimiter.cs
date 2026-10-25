namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     Admits at most the configured requests per minute for each token and service group, and holds a group back after
///     the broker reported its limit exhausted until the reported reset. Waiting happens before a request is sent, so a
///     write never waits after it may have left the process.
/// </summary>
internal sealed class TInvestRequestLimiter(TInvestRequestLimits limits, TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Dictionary<(string Token, TInvestMethodGroup Group), Queue<DateTimeOffset>> _admitted =
        new Dictionary<(string Token, TInvestMethodGroup Group), Queue<DateTimeOffset>>();

    private readonly Lock _gate = new Lock();

    private readonly Dictionary<(string Token, TInvestMethodGroup Group), DateTimeOffset> _pausedUntil =
        new Dictionary<(string Token, TInvestMethodGroup Group), DateTimeOffset>();

    public async Task Admit(TInvestToken token, TInvestMethodGroup group, CancellationToken cancellationToken)
    {
        (string, TInvestMethodGroup) key = (token.Fingerprint, group);

        while (true)
        {
            TimeSpan wait;

            lock (_gate)
            {
                DateTimeOffset now = clock.GetUtcNow();
                Queue<DateTimeOffset> admitted = _admitted.TryGetValue(key, out Queue<DateTimeOffset>? existing)
                    ? existing
                    : _admitted[key] = new Queue<DateTimeOffset>();

                while (admitted.Count > 0 && admitted.Peek() + Window <= now)
                {
                    admitted.Dequeue();
                }

                if (_pausedUntil.TryGetValue(key, out DateTimeOffset until) && until > now)
                {
                    wait = until - now;
                }
                else if (admitted.Count < Limit(group))
                {
                    admitted.Enqueue(now);

                    return;
                }
                else
                {
                    wait = admitted.Peek() + Window - now;
                }
            }

            await Task.Delay(wait, clock, cancellationToken);
        }
    }

    /// <summary>
    ///     Holds the group back for <paramref name="reset" /> after the broker refused a request for its limit.
    /// </summary>
    public void Pause(TInvestToken token, TInvestMethodGroup group, TimeSpan reset)
    {
        lock (_gate)
        {
            DateTimeOffset until = clock.GetUtcNow() + reset;
            (string, TInvestMethodGroup) key = (token.Fingerprint, group);
            _pausedUntil[key] = _pausedUntil.TryGetValue(key, out DateTimeOffset current) && current > until ? current : until;
        }
    }

    private int Limit(TInvestMethodGroup group)
    {
        return group switch
        {
            TInvestMethodGroup.Users => limits.Users,
            TInvestMethodGroup.Instruments => limits.Instruments,
            TInvestMethodGroup.MarketData => limits.MarketData,
            TInvestMethodGroup.Orders => limits.Orders,
            TInvestMethodGroup.Operations => limits.Operations,
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown T-Invest service group.")
        };
    }
}