using System.Runtime.CompilerServices;
using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots.MarkFeedStale;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     Bybit Spot live closed candles from the public WebSocket: one feed per series in this process, fanned out to every
///     subscriber. A subscriber that names its last candle receives the gap from public history first.
/// </summary>
internal sealed class BybitLiveCandleSource(
    IBybitPublicStreamTransport transport,
    IServiceScopeFactory scopes,
    BybitOptions options,
    TimeProvider clock,
    ILogger<BybitLiveCandleSource> logger,
    BybitMarket? market = null) : ILiveCandleSource
{
    private readonly BybitMarket _market = market ?? BybitMarket.Spot;
    private readonly Dictionary<(string Symbol, string Timeframe), BybitLiveCandleFeed> _feeds = [];
    private readonly Lock _gate = new Lock();

    public VenueId Venue => _market.Venue;

    public async IAsyncEnumerable<Candle> Subscribe(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset? afterOpen,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (afterOpen is { } cursor && !timeframe.IsAligned(cursor))
        {
            throw new ArgumentException("The candle cursor is not on the timeframe grid.", nameof(afterOpen));
        }

        var subscriber = Channel.CreateUnbounded<Candle>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        BybitLiveCandleFeed feed = Join(symbol, timeframe, subscriber.Writer);
        var history = new BybitLiveHistory(ReadHistory);

        try
        {
            await foreach (Candle live in subscriber.Reader.ReadAllAsync(cancellationToken))
            {
                if (afterOpen is { } last)
                {
                    if (live.OpenTime <= last)
                    {
                        continue;
                    }

                    foreach (Candle missing in await history.After(symbol, timeframe, last, live.OpenTime, cancellationToken))
                    {
                        yield return missing;
                    }
                }

                yield return live;
                afterOpen = live.OpenTime;
            }
        }
        finally
        {
            Leave(symbol, timeframe, feed, subscriber.Writer);
        }
    }

    private BybitLiveCandleFeed Join(InstrumentSymbol symbol, Timeframe timeframe, ChannelWriter<Candle> subscriber)
    {
        lock (_gate)
        {
            if (!_feeds.TryGetValue((symbol.Value, timeframe.Key), out BybitLiveCandleFeed? feed))
            {
                feed = new BybitLiveCandleFeed(
                    symbol,
                    timeframe,
                    timeframe.Duration + options.LiveFeedStallMargin,
                    options.LiveFeedReconnectDelay,
                    transport,
                    new BybitLiveHistory(ReadHistory),
                    (stale, token) => ReportStale(symbol, timeframe, stale, token),
                    clock,
                    logger);
                _feeds.Add((symbol.Value, timeframe.Key), feed);
                feed.Add(subscriber);
                feed.Start();

                return feed;
            }

            feed.Add(subscriber);

            return feed;
        }
    }

    private void Leave(InstrumentSymbol symbol, Timeframe timeframe, BybitLiveCandleFeed feed, ChannelWriter<Candle> subscriber)
    {
        lock (_gate)
        {
            feed.Remove(subscriber);

            if (feed.Count == 0 && _feeds.TryGetValue((symbol.Value, timeframe.Key), out BybitLiveCandleFeed? current) && ReferenceEquals(current, feed))
            {
                _feeds.Remove((symbol.Value, timeframe.Key));
                feed.Stop();
            }
        }
    }

    private async Task<IReadOnlyList<Candle>> ReadHistory(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        IHistoricalCandleSource source = scope.ServiceProvider.GetServices<IHistoricalCandleSource>().First(candidate => candidate.Venue == _market.Venue);

        return (await source.Get(symbol, timeframe, from, to, cancellationToken)).Candles;
    }

    private async Task ReportStale(InstrumentSymbol symbol, Timeframe timeframe, bool stale, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new MarkFeedStaleCommand(_market.Venue.Value, symbol.Value, timeframe.Key, stale), cancellationToken);
    }
}