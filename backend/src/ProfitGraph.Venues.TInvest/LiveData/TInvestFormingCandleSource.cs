using System.Runtime.CompilerServices;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using Candle = Tinkoff.InvestApi.V1.Candle;

namespace ProfitGraph.Venues.TInvest.LiveData;

/// <summary>
///     Updates of a share's forming candle from the market data stream (ADR 0026), with the caller's T-Invest credential, which
///     the feed opens around the subscription. The stream has no close flag, so every update reports the candle as not closed
///     and the next open time ends it. A share's lots become shares as in history; an update that is not a valid candle of the
///     timeframe is dropped, because one bad message must not end the stream of every chart on the token.
/// </summary>
internal sealed class TInvestFormingCandleSource(TInvestShares shares, TInvestCandleStreams streams, TimeProvider clock) : IFormingCandleSource
{
    public VenueId Venue => TInvestVenue.VenueId;

    public bool Supports(Timeframe timeframe)
    {
        return Interval(timeframe) is not null;
    }

    public async IAsyncEnumerable<FormingCandleInternal> Subscribe(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        SubscriptionInterval interval = Interval(timeframe) ?? throw new ArgumentException("T-Invest has no live candle for this timeframe.", nameof(timeframe));
        TInvestToken token = TInvestMarketDataToken.Current() ?? throw new InvalidOperationException("T-Invest live candles are read with a T-Invest credential.");

        if (!TInvestSymbol.TryParse(symbol.Value, out TInvestSymbol? parsed))
        {
            throw new ArgumentException("The symbol is not a T-Invest share.", nameof(symbol));
        }

        TInvestShare share = await shares.Get(token, parsed, cancellationToken);

        await foreach (Candle update in streams.Subscribe(token, share.Uid, interval, cancellationToken))
        {
            if (Convert(update, share, timeframe) is { } converted)
            {
                yield return converted;
            }
        }
    }

    private static SubscriptionInterval? Interval(Timeframe timeframe)
    {
        return timeframe.Key switch
        {
            "1m" => SubscriptionInterval.OneMinute,
            "3m" => SubscriptionInterval._3Min,
            "5m" => SubscriptionInterval.FiveMinutes,
            "15m" => SubscriptionInterval.FifteenMinutes,
            "30m" => SubscriptionInterval._30Min,
            "1h" => SubscriptionInterval.OneHour,
            "2h" => SubscriptionInterval._2Hour,
            "4h" => SubscriptionInterval._4Hour,
            _ => null
        };
    }

    private FormingCandleInternal? Convert(Candle update, TInvestShare share, Timeframe timeframe)
    {
        if (update.Time is null || update.Open is null || update.High is null || update.Low is null || update.Close is null ||
            update.CandleSourceType is not (CandleSource.Exchange or CandleSource.Unspecified))
        {
            return null;
        }

        var candle = new Engine.Market.Candle(
            update.Time.ToUtc(),
            update.Open.ToDecimal(),
            update.High.ToDecimal(),
            update.Low.ToDecimal(),
            update.Close.ToDecimal(),
            update.Volume * share.Lot);

        return CandleValidator.IsValid(candle, timeframe)
            ? new FormingCandleInternal(
                candle.OpenTime,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume,
                IsClosed: false,
                update.LastTradeTs?.ToUtc() ?? clock.GetUtcNow())
            : null;
    }
}