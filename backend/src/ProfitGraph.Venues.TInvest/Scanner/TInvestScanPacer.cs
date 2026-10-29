using ProfitGraph.Application.Scanner;

namespace ProfitGraph.Venues.TInvest.Scanner;

/// <summary>
///     A share of a T-Invest per-minute limit for the market scanner (ADR 0062): turns are taken one after another and spaced
///     evenly, so at most the given count goes out in any minute; a turn of several units waits as long as that many. After the
///     broker reported the limit exhausted, no turn starts until the pause ends. One instance per limit serves the whole process.
/// </summary>
internal sealed class TInvestScanPacer(int perMinute, TimeProvider clock)
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(initialCount: 1, maxCount: 1);
    private readonly Lock _pause = new Lock();
    private readonly TimeSpan _spacing = TimeSpan.FromTicks(TimeSpan.TicksPerMinute / perMinute);
    private DateTimeOffset _nextStart = DateTimeOffset.MinValue;
    private DateTimeOffset? _pausedUntil;

    public DateTimeOffset? PausedUntil
    {
        get
        {
            lock (_pause)
            {
                return _pausedUntil is { } until && until > clock.GetUtcNow() ? until : null;
            }
        }
    }

    /// <summary>
    ///     Waits for a turn of <paramref name="units" />; throws <see cref="MarketScanErrorCodes.RequestsPaused" /> during a pause.
    /// </summary>
    public async Task Take(int units, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            ThrowIfPaused();
            DateTimeOffset now = clock.GetUtcNow();

            if (_nextStart > now)
            {
                await Task.Delay(_nextStart - now, clock, cancellationToken);
                now = clock.GetUtcNow();
            }

            ThrowIfPaused();
            _nextStart = now + _spacing * units;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Pause(TimeSpan duration)
    {
        lock (_pause)
        {
            DateTimeOffset until = clock.GetUtcNow() + duration;
            _pausedUntil = _pausedUntil is { } current && current > until ? current : until;
        }
    }

    private void ThrowIfPaused()
    {
        if (PausedUntil is { } until)
        {
            throw new MarketScanException(MarketScanErrorCodes.RequestsPaused, $"T-Invest reported the scanner's limit exhausted; requests resume at {until:O}.");
        }
    }
}