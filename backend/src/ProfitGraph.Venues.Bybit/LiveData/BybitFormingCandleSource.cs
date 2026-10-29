using System.Runtime.CompilerServices;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     Bybit Spot kline updates of the candle being formed, from the same public WebSocket topic as the closed candles (ADR
///     0026). Every update of the topic is passed on with Bybit's own confirm flag as the closed flag; nothing is stored, and the
///     connection is never authenticated. The caller owns reconnecting, so each subscription is one connection.
/// </summary>
internal sealed class BybitFormingCandleSource(IBybitPublicStreamTransport transport, BybitMarket? market = null) : IFormingCandleSource
{
    private readonly BybitMarket _market = market ?? BybitMarket.Spot;
    public VenueId Venue => _market.Venue;

    public bool Supports(Timeframe timeframe)
    {
        return BybitKlineMessages.Supports(timeframe);
    }

    public async IAsyncEnumerable<FormingCandleInternal> Subscribe(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string topic = BybitKlineMessages.Topic(symbol.Value, timeframe);

        await foreach (string message in transport.Connect(topic, cancellationToken))
        {
            foreach (FormingCandleInternal update in BybitKlineMessages.Updates(message, topic, timeframe))
            {
                yield return update;
            }
        }
    }
}