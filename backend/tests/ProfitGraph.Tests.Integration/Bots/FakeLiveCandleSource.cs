using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Live candles of a venue, the test venue unless named, that a test pushes or fails by hand; every subscription reads
///     the same pushes.
/// </summary>
public sealed class FakeLiveCandleSource(VenueId venue) : ILiveCandleSource
{
    private readonly ConcurrentDictionary<Channel<Candle>, byte> _subscribers = new ConcurrentDictionary<Channel<Candle>, byte>();
    private Exception? _failure;

    public int Subscribers => _subscribers.Count;

    public VenueId Venue { get; } = venue;

    public FakeLiveCandleSource() : this(new VenueId(TestHistoryVenue.WithHistory))
    {
    }

    public async IAsyncEnumerable<Candle> Subscribe(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset? afterOpen,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<Candle>();
        _subscribers.TryAdd(channel, value: 0);

        try
        {
            await foreach (Candle candle in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (afterOpen is null || candle.OpenTime > afterOpen)
                {
                    yield return candle;
                }
            }
        }
        finally
        {
            _subscribers.TryRemove(channel, out _);
        }
    }

    public void Push(Candle candle)
    {
        foreach (Channel<Candle> subscriber in _subscribers.Keys)
        {
            subscriber.Writer.TryWrite(candle);
        }
    }

    /// <summary>
    ///     Breaks every current and later subscription.
    /// </summary>
    public void Fail(Exception failure)
    {
        Volatile.Write(ref _failure, failure);

        foreach (Channel<Candle> subscriber in _subscribers.Keys)
        {
            subscriber.Writer.TryComplete(failure);
        }
    }
}