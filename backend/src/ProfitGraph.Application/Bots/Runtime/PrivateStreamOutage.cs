namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     The losses of one bot's private order stream under <see cref="PrivateStreamReconnectPolicy" /> (ADR 0044): when the
///     outage began, how many attempts it took and when the stream was last restored.
/// </summary>
public sealed class PrivateStreamOutage(PrivateStreamReconnectPolicy policy, TimeProvider clock)
{
    private DateTimeOffset? _since;
    private int _attempts;
    private DateTimeOffset? _restoredAt;

    /// <summary>
    ///     Records that the stream was lost or could not be restored. Returns the pause before the next attempt, or null when
    ///     the outage has lasted the whole budget and the bot's updates must end.
    /// </summary>
    public TimeSpan? Lost()
    {
        DateTimeOffset now = clock.GetUtcNow();

        if (_since is not { } since || (_restoredAt is { } restoredAt && now - restoredAt >= policy.StableAfter))
        {
            since = now;
            _since = now;
            _attempts = 0;
        }

        _restoredAt = null;

        if (now - since >= policy.Budget)
        {
            return null;
        }

        return policy.Delays[Math.Min(_attempts++, policy.Delays.Count - 1)];
    }

    /// <summary>
    ///     Records that the venue confirmed the stream again and the bot's orders were read again.
    /// </summary>
    public void Restored()
    {
        _restoredAt = clock.GetUtcNow();
    }
}