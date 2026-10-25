using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     The Worker's <c>live</c> role keeps one display-price subscription per instrument of the bots that should run
///     (S5-19) and stores at most one observation per instrument a second; storing signals <c>live_mark_price</c>. A lost
///     subscription is opened again after a pause.
/// </summary>
internal sealed class LiveMarkPricePublisher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<LiveMarkPricePublisher> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StoreInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly Dictionary<LiveInstrument, (CancellationTokenSource Stop, Task Work)> _subscriptions = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval, clock);

        try
        {
            do
            {
                try
                {
                    await Refresh(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Refreshing display-price subscriptions failed with {ExceptionType}.", exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        finally
        {
            foreach ((CancellationTokenSource stop, _) in _subscriptions.Values)
            {
                await stop.CancelAsync();
            }

            await Task.WhenAll(_subscriptions.Values.Select(subscription => subscription.Work));
        }
    }

    private async Task Refresh(CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        HashSet<LiveInstrument> wanted = [.. await scope.ServiceProvider.GetRequiredService<IBotRepository>().ListDesiredRunningInstruments(stoppingToken)];
        var sources = scope.ServiceProvider.GetRequiredService<VenuePorts<ILiveMarkPriceSource>>();

        foreach (LiveInstrument instrument in _subscriptions.Keys.Where(instrument => !wanted.Contains(instrument)).ToArray())
        {
            (CancellationTokenSource stop, Task work) = _subscriptions[instrument];
            await stop.CancelAsync();
            await work;
            stop.Dispose();
            _subscriptions.Remove(instrument);
        }

        foreach (LiveInstrument instrument in wanted.Where(instrument => !_subscriptions.ContainsKey(instrument)))
        {
            if (sources.Find(instrument.Venue) is not { } source)
            {
                continue;
            }

            var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _subscriptions[instrument] = (stop, Follow(source, instrument, stop.Token));
        }
    }

    private async Task Follow(ILiveMarkPriceSource source, LiveInstrument instrument, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var lastStored = DateTimeOffset.MinValue;

            try
            {
                await foreach (LiveMarkPriceInternal price in source.Subscribe(instrument.Symbol, cancellationToken))
                {
                    if (clock.GetUtcNow() - lastStored < StoreInterval)
                    {
                        continue;
                    }

                    using IServiceScope scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ILiveMarkPriceRepository>()
                        .Store(new LiveMarkPrice(instrument.Venue, instrument.Symbol, price.Price, price.ObservedAt), cancellationToken);
                    lastStored = clock.GetUtcNow();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("The display price of {Symbol} failed with {ExceptionType}; it is followed again.", instrument.Symbol.Value, exception.GetType().Name);
            }

            try
            {
                await Task.Delay(ReconnectDelay, clock, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}