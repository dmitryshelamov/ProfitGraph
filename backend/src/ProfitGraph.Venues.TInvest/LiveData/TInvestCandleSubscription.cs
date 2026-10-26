using System.Threading.Channels;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.LiveData;

/// <summary>
///     One reader's subscription to a share's candles of an interval on a shared market data stream; the stream's messages for
///     it are written to <see cref="Inbox" />, which ends with the stream's error when the stream is lost.
/// </summary>
internal sealed class TInvestCandleSubscription(string instrumentUid, SubscriptionInterval interval)
{
    public string InstrumentUid => instrumentUid;

    public SubscriptionInterval Interval => interval;

    public Channel<Candle> Inbox { get; } = Channel.CreateUnbounded<Candle>(new UnboundedChannelOptions { SingleReader = true });

    public bool Matches(Candle candle)
    {
        return candle.InstrumentUid == instrumentUid && candle.Interval == interval;
    }
}