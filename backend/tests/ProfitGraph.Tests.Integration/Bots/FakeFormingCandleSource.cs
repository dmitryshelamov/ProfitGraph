using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The test venue's forming candle, fed by hand: each subscription is one connection that lasts until its reader stops.
/// </summary>
internal sealed class FakeFormingCandleSource : IFormingCandleSource
{
    private readonly List<Channel<FormingCandleInternal>> _connections = [];
    private int _open;

    public int Subscriptions
    {
        get
        {
            lock (_connections)
            {
                return _connections.Count;
            }
        }
    }

    public int Open => Volatile.Read(ref _open);

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public bool Supports(Timeframe timeframe)
    {
        return true;
    }

    public async IAsyncEnumerable<FormingCandleInternal> Subscribe(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<FormingCandleInternal>();

        lock (_connections)
        {
            _connections.Add(channel);
        }

        Interlocked.Increment(ref _open);

        try
        {
            await foreach (FormingCandleInternal candle in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return candle;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }

    public void Push(FormingCandleInternal candle)
    {
        lock (_connections)
        {
            _connections[^1].Writer.TryWrite(candle);
        }
    }
}