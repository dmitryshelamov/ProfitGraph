using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using Candle = ProfitGraph.Engine.Market.Candle;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     Reads a share's real, complete exchange candles of [from, to) in windows (<c>MarketDataService/GetCandles</c> with the
///     <c>Exchange</c> source and no <c>limit</c>, which the broker refuses together, <c>30220</c>). Volume in lots becomes
///     shares; a candle off the grid, of another source or inconsistent is invalid provider data, never shifted or dropped.
/// </summary>
internal sealed class TInvestCandleReader(TInvestTransport transport)
{
    public async Task<Dictionary<DateTimeOffset, Candle>> Read(
        TInvestToken token,
        TInvestShare share,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        (CandleInterval interval, TimeSpan window) = TInvestCandleWindows.For(timeframe);
        var candles = new Dictionary<DateTimeOffset, Candle>();

        for (DateTimeOffset start = from; start < to;)
        {
            DateTimeOffset end = start + window < to ? start + window : to;
            GetCandlesResponse response = await Page(token, share, interval, start, end, cancellationToken);

            foreach (HistoricCandle candle in response.Candles.Where(candle => candle.IsComplete))
            {
                Candle converted = Convert(candle, share, timeframe, start, end);

                if (!candles.TryAdd(converted.OpenTime, converted) && candles[converted.OpenTime] != converted)
                {
                    throw Invalid(HistoricalDataErrorCodes.Conflict, "T-Invest returned conflicting candles.", start, end);
                }
            }

            start = end;
        }

        return candles;
    }

    private async Task<GetCandlesResponse> Page(
        TInvestToken token,
        TInvestShare share,
        CandleInterval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transport.Unary(
                token,
                TInvestMethodGroup.MarketData,
                (invoker, options) => new MarketDataService.MarketDataServiceClient(invoker).GetCandlesAsync(
                    new GetCandlesRequest
                    {
                        InstrumentId = share.Uid,
                        From = from.ToTimestamp(),
                        To = to.ToTimestamp(),
                        Interval = interval,
                        CandleSourceType = GetCandlesRequest.Types.CandleSource.Exchange
                    },
                    options),
                cancellationToken);
        }
        catch (TInvestRpcException error) when (error.Kind is TInvestFailureKind.Unauthenticated or TInvestFailureKind.PermissionDenied)
        {
            throw Invalid(HistoricalDataErrorCodes.Unavailable, "T-Invest refused the credential's token.", from, to);
        }
        catch (TInvestRpcException)
        {
            throw Invalid(HistoricalDataErrorCodes.ProviderUnavailable, "T-Invest history is unavailable; try again later.", from, to);
        }
    }

    private static Candle Convert(HistoricCandle candle, TInvestShare share, Timeframe timeframe, DateTimeOffset from, DateTimeOffset to)
    {
        if (candle.CandleSource != CandleSource.Exchange || candle.Time is null)
        {
            throw Invalid(HistoricalDataErrorCodes.InvalidProviderData, "T-Invest returned a candle that is not an exchange candle.", from, to);
        }

        var converted = new Candle(
            candle.Time.ToUtc(),
            candle.Open.ToDecimal(),
            candle.High.ToDecimal(),
            candle.Low.ToDecimal(),
            candle.Close.ToDecimal(),
            candle.Volume * share.Lot);

        return CandleValidator.IsValid(converted, timeframe) && converted.OpenTime >= from && converted.OpenTime < to
            ? converted
            : throw Invalid(HistoricalDataErrorCodes.InvalidProviderData, "T-Invest returned an invalid or misaligned candle.", from, to);
    }

    private static HistoricalDataException Invalid(string code, string message, DateTimeOffset from, DateTimeOffset to)
    {
        return new HistoricalDataException(code, message, [new CandleRangeInternal(from, to)]);
    }
}