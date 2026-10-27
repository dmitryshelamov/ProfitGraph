using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Infrastructure.Bots;

/// <summary>
///     A forming-candle source the test feeds by hand: each subscription is one connection that ends when the test completes
///     or fails it, or when the feed stops reading.
/// </summary>
internal sealed class FakeFormingCandleSource : IFormingCandleSource
{
    private readonly List<Channel<FormingCandleInternal>> _connections = [];
    private int _open;

    public int Subscriptions => _connections.Count;

    public int Open => Volatile.Read(ref _open);

    public ExchangeCredentialSecretsInternal? SeenAccess { get; private set; }

    public VenueId Venue => new VenueId("test-venue");

    public bool Supports(Timeframe timeframe)
    {
        return timeframe.Key != "12h";
    }

    public async IAsyncEnumerable<FormingCandleInternal> Subscribe(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<FormingCandleInternal>();
        SeenAccess = MarketDataAccess.Current;

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
        Current().Writer.TryWrite(candle);
    }

    public void Fail()
    {
        Current().Writer.TryComplete(new IOException("The venue connection was lost."));
    }

    private Channel<FormingCandleInternal> Current()
    {
        lock (_connections)
        {
            return _connections[^1];
        }
    }
}