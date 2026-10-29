using System.Runtime.CompilerServices;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     The last-trade price of a Spot symbol from Bybit's public ticker stream (S5-19); the connection is never
///     authenticated. The stream ends when Bybit closes the connection.
/// </summary>
internal sealed class BybitLiveMarkPriceSource(IBybitPublicStreamTransport transport, BybitMarket? market = null) : ILiveMarkPriceSource
{
    private readonly BybitMarket _market = market ?? BybitMarket.Spot;
    public VenueId Venue => _market.Venue;

    public async IAsyncEnumerable<LiveMarkPriceInternal> Subscribe(InstrumentSymbol symbol, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string topic = BybitTickerMessages.Topic(symbol.Value);

        await foreach (string message in transport.Connect(topic, cancellationToken))
        {
            if (BybitTickerMessages.Price(message, topic) is { } price)
            {
                yield return price;
            }
        }
    }
}