using System.Text.Json;

using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     The offset between Bybit's public server time and this host, measured at the midpoint of a fast request and reused
///     for a while; a timestamp rejection (<c>10002</c>) measures it again.
/// </summary>
internal sealed class BybitServerClock(BybitOptions options, TimeProvider clock)
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(initialCount: 1, maxCount: 1);
    private (long Offset, DateTimeOffset MeasuredAt)? _offset;

    public async Task<long?> Now(Func<CancellationToken, Task<BybitRestResult>> readServerTime, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_offset is not { } known || clock.GetUtcNow() - known.MeasuredAt > options.ServerClockLifetime)
            {
                DateTimeOffset before = clock.GetUtcNow();
                BybitRestResult result = await readServerTime(cancellationToken);
                DateTimeOffset after = clock.GetUtcNow();

                if (result.Outcome != BybitRestOutcome.Accepted || after - before > options.ServerClockMaxRoundTrip ||
                    !result.Body.TryGetProperty("time", out JsonElement time) || !time.TryGetInt64(out long server))
                {
                    return null;
                }

                long midpoint = before.ToUnixTimeMilliseconds() + (long)(after - before).TotalMilliseconds / 2;
                _offset = known = (server - midpoint, after);
            }

            return clock.GetUtcNow().ToUnixTimeMilliseconds() + known.Offset;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _offset = null;
    }
}