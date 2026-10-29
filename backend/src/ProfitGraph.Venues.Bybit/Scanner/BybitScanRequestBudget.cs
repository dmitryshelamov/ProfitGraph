using ProfitGraph.Application.Scanner;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Scanner;

/// <summary>
///     The market scanner's share of Bybit's per-IP REST limit (ADR 0059): requests start at most
///     <see cref="BybitOptions.ScannerRequestsPerSecond" /> a second, one after another, and after a 403 none starts for
///     <see cref="BybitOptions.ScannerBanPause" />. One instance serves the whole process.
/// </summary>
internal sealed class BybitScanRequestBudget(BybitOptions options, TimeProvider clock)
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(initialCount: 1, maxCount: 1);
    private readonly Lock _pause = new Lock();
    private readonly TimeSpan _spacing = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / options.ScannerRequestsPerSecond);
    private DateTimeOffset _nextStart = DateTimeOffset.MinValue;
    private DateTimeOffset? _pausedUntil;

    /// <summary>
    ///     The end of the current ban pause, or null when requests may go.
    /// </summary>
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
    ///     Waits for the next request's turn; throws <see cref="MarketScanErrorCodes.RequestsPaused" /> during a ban pause.
    /// </summary>
    public async Task Take(CancellationToken cancellationToken)
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
            _nextStart = now + _spacing;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Pause()
    {
        lock (_pause)
        {
            _pausedUntil = clock.GetUtcNow() + options.ScannerBanPause;
        }
    }

    private void ThrowIfPaused()
    {
        if (PausedUntil is { } until)
        {
            throw new MarketScanException(MarketScanErrorCodes.RequestsPaused, $"Bybit banned the server's IP; scanner requests resume at {until:O}.");
        }
    }
}