namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     The request queue of one Steam account (Profit Fox Designer ADR 0083, problems 2 to 4): one physical request in
///     flight, the highest waiting class first, history only when nothing else waits, writes spaced apart. A rate limit of
///     a quote or history pauses those classes; any other pauses the whole account. Pauses wait outside the gate, so a
///     request can still join the queue, and a success never shortens a pause.
/// </summary>
internal sealed class SteamAccountQueue(SteamOptions options, TimeProvider clock)
{
    private readonly object _lock = new object();
    private readonly List<SteamQueuedRequest> _waiting = new List<SteamQueuedRequest>();
    private TaskCompletionSource _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _busy;
    private long _sequence;
    private DateTimeOffset _accountPausedUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _backgroundPausedUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _nextWriteAt = DateTimeOffset.MinValue;
    private int _accountStreak;
    private int _backgroundStreak;

    public DateTimeOffset AccountPausedUntil
    {
        get
        {
            lock (_lock)
            {
                return _accountPausedUntil;
            }
        }
    }

    /// <summary>
    ///     Waits for this request's turn; false when its deadline passed first, and then it holds nothing.
    /// </summary>
    public async Task<bool> Enter(SteamRequestClass requestClass, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = clock.GetUtcNow() + options.QueueDeadline;
        SteamQueuedRequest waiter;

        lock (_lock)
        {
            waiter = new SteamQueuedRequest(requestClass, ++_sequence);
            _waiting.Add(waiter);
        }

        try
        {
            while (true)
            {
                Task changed;
                DateTimeOffset wake;

                lock (_lock)
                {
                    DateTimeOffset now = clock.GetUtcNow();

                    if (now >= deadline)
                    {
                        _waiting.Remove(waiter);
                        Signal();

                        return false;
                    }

                    if (!_busy && Next(now) == waiter)
                    {
                        _waiting.Remove(waiter);
                        _busy = true;
                        Signal();

                        return true;
                    }

                    // Waiting for a pause to end wakes at its end; waiting for the gate or a higher class waits for a signal.
                    changed = _changed.Task;
                    DateTimeOffset eligible = EligibleAt(waiter.Class, now);
                    wake = eligible > now ? Min(deadline, eligible) : deadline;
                }

                using var delay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                TimeSpan wait = wake - clock.GetUtcNow();
                Task timer = wait > TimeSpan.Zero ? Task.Delay(wait, clock, delay.Token) : Task.CompletedTask;
                await Task.WhenAny(changed, timer);
                await delay.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch
        {
            lock (_lock)
            {
                if (_waiting.Remove(waiter))
                {
                    Signal();
                }
            }

            throw;
        }
    }

    /// <summary>
    ///     Ends the turn with what the request answered.
    /// </summary>
    public void Leave(SteamRequestClass requestClass, SteamRequestResult result)
    {
        lock (_lock)
        {
            DateTimeOffset now = clock.GetUtcNow();
            _busy = false;
            bool background = requestClass is SteamRequestClass.Quote or SteamRequestClass.History;

            if (requestClass == SteamRequestClass.Write && result.Outcome != SteamRequestOutcome.NotSent)
            {
                _nextWriteAt = now + options.WriteSpacing;
            }

            if (result.Outcome == SteamRequestOutcome.RateLimited)
            {
                if (background)
                {
                    _backgroundPausedUntil = Max(_backgroundPausedUntil, now + Pause(_backgroundStreak++, result.RetryAfter));
                }
                else
                {
                    _accountPausedUntil = Max(_accountPausedUntil, now + Pause(_accountStreak++, result.RetryAfter));
                }
            }
            else if (result.Outcome == SteamRequestOutcome.Success)
            {
                if (background)
                {
                    _backgroundStreak = 0;
                }
                else
                {
                    _accountStreak = 0;
                }
            }

            Signal();
        }
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }

    private TimeSpan Pause(int streak, TimeSpan? retryAfter)
    {
        TimeSpan exponential = options.InitialCooldown * Math.Pow(x: 2, Math.Min(streak, val2: 16));
        TimeSpan capped = exponential < options.MaximumCooldown ? exponential : options.MaximumCooldown;

        return retryAfter is { } provider && provider > capped ? provider : capped;
    }

    /// <summary>
    ///     The waiter to serve now: the highest eligible class, then the earliest; history only when no other class waits.
    /// </summary>
    private SteamQueuedRequest? Next(DateTimeOffset now)
    {
        bool othersWait = _waiting.Any(waiter => waiter.Class != SteamRequestClass.History);

        return _waiting
            .Where(waiter => EligibleAt(waiter.Class, now) <= now && (waiter.Class != SteamRequestClass.History || !othersWait))
            .OrderBy(waiter => waiter.Class)
            .ThenBy(waiter => waiter.Sequence)
            .FirstOrDefault();
    }

    private DateTimeOffset EligibleAt(SteamRequestClass requestClass, DateTimeOffset now)
    {
        DateTimeOffset at = _accountPausedUntil;

        if (requestClass is SteamRequestClass.Quote or SteamRequestClass.History)
        {
            at = Max(at, _backgroundPausedUntil);
        }

        if (requestClass == SteamRequestClass.Write)
        {
            at = Max(at, _nextWriteAt);
        }

        return at > now ? at : now;
    }

    private void Signal()
    {
        TaskCompletionSource previous = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }
}