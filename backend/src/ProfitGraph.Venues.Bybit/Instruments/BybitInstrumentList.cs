using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Venues.Bybit.Instruments;

/// <summary>
///     The process-wide copy of Bybit's tradable Spot pairs; one reader refreshes it at a time, and the previous copy is
///     kept when a refresh fails.
/// </summary>
internal sealed class BybitInstrumentList(TimeProvider clock, TimeSpan lifetime)
{
    private readonly SemaphoreSlim _refresh = new SemaphoreSlim(initialCount: 1, maxCount: 1);
    private IReadOnlyList<InstrumentInternal>? _instruments;
    private DateTimeOffset _loadedAt;

    public async Task<IReadOnlyList<InstrumentInternal>> Get(Func<CancellationToken, Task<IReadOnlyList<InstrumentInternal>>> load, CancellationToken cancellationToken)
    {
        IReadOnlyList<InstrumentInternal>? current = Fresh();

        if (current is not null)
        {
            return current;
        }

        await _refresh.WaitAsync(cancellationToken);

        try
        {
            current = Fresh();

            if (current is not null)
            {
                return current;
            }

            try
            {
                IReadOnlyList<InstrumentInternal> loaded = await load(cancellationToken);
                Volatile.Write(ref _instruments, loaded);
                _loadedAt = clock.GetUtcNow();

                return loaded;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _instruments) is { } stale)
            {
                return stale;
            }
        }
        finally
        {
            _refresh.Release();
        }
    }

    private IReadOnlyList<InstrumentInternal>? Fresh()
    {
        IReadOnlyList<InstrumentInternal>? instruments = Volatile.Read(ref _instruments);

        return instruments is not null && clock.GetUtcNow() - _loadedAt < lifetime ? instruments : null;
    }
}