using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using Grpc.Core;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using Candle = Tinkoff.InvestApi.V1.Candle;

namespace ProfitGraph.Venues.TInvest.Scanner;

/// <summary>
///     T-Invest MOEX shares for the market scanner (ADR 0062), read with the scanner's credential: the main-board rouble shares
///     that API trading allows to buy and sell, exchange candles of the regular weekday sessions (weekend sessions are closures,
///     as in history, ADR 0023), and closed 1-minute candles over the market data stream. Volume in lots becomes shares and
///     turnover is volume times the typical price, since T-Invest candles carry no turnover. T-Invest has no quote snapshot for
///     many shares at once, so spreads stay empty. Every request takes a turn of the scanner's request budget and a stream's
///     subscriptions a turn of its subscription budget; the stream reports itself alive on the broker's pings, because a closed
///     market sends no candles.
/// </summary>
internal sealed class TInvestMarketScanSource(
    TInvestTransport transport,
    TInvestShares shares,
    TInvestScanMarketData marketData,
    TInvestScanBudget budget,
    IOptions<TInvestOptions> options) : IMarketScanSource
{
    private readonly ConcurrentDictionary<string, TInvestShare> _shares = new ConcurrentDictionary<string, TInvestShare>(StringComparer.Ordinal);

    public VenueId Venue => TInvestVenue.VenueId;

    public int MaxSymbolsPerStream => options.Value.Scanner.SharesPerStream;

    public IReadOnlyList<ScanPresetInternal> BuiltInPresets => TInvestScanPresets.All;

    public string QuoteAsset => "RUB";

    public async Task<IReadOnlyList<ScanInstrumentInternal>> Universe(CancellationToken cancellationToken)
    {
        TInvestToken token = await marketData.Token(cancellationToken);

        SharesResponse response = await Execute(
            "share list",
            token,
            TInvestMethodGroup.Instruments,
            (invoker, callOptions) => new InstrumentsService.InstrumentsServiceClient(invoker).SharesAsync(
                new InstrumentsRequest { InstrumentStatus = InstrumentStatus.Base },
                callOptions),
            cancellationToken);
        var universe = new SortedDictionary<string, ScanInstrumentInternal>(StringComparer.Ordinal);

        foreach (Share share in response.Instruments.Where(IsScanned))
        {
            if (!TInvestSymbol.TryParse(share.Ticker, out TInvestSymbol? symbol) || symbol.Ticker != share.Ticker)
            {
                continue;
            }

            string name = symbol.Symbol.Value;
            _shares[name] = new TInvestShare(
                symbol,
                share.Uid,
                share.Name,
                share.Lot,
                share.MinPriceIncrement.ToDecimal(),
                FirstMinuteCandle: null,
                FirstDayCandle: null);
            universe[name] = new ScanInstrumentInternal(name, share.IpoDate is { Seconds: > 0 } ipo ? ipo.ToUtc() : null);
        }

        return [.. universe.Values];
    }

    public async Task<IReadOnlyList<ScanCandle>> Candles(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        TInvestToken token = await marketData.Token(cancellationToken);
        TInvestShare share = await Share(token, symbol, cancellationToken);
        (CandleInterval interval, TimeSpan window) = TInvestCandleWindows.For(timeframe);
        var candles = new SortedDictionary<DateTimeOffset, ScanCandle>();

        for (DateTimeOffset start = from; start < to;)
        {
            DateTimeOffset end = start + window < to ? start + window : to;
            DateTimeOffset pageStart = start;

            GetCandlesResponse response = await Execute(
                "candle",
                token,
                TInvestMethodGroup.MarketData,
                (invoker, callOptions) => new MarketDataService.MarketDataServiceClient(invoker).GetCandlesAsync(
                    new GetCandlesRequest
                    {
                        InstrumentId = share.Uid,
                        From = pageStart.ToTimestamp(),
                        To = end.ToTimestamp(),
                        Interval = interval,
                        CandleSourceType = GetCandlesRequest.Types.CandleSource.Exchange
                    },
                    callOptions),
                cancellationToken);

            foreach (HistoricCandle row in response.Candles.Where(row => row.IsComplete))
            {
                ScanCandle candle = row.CandleSource == CandleSource.Exchange && row.Time is not null &&
                    TInvestScanCandles.From(row.Time.ToUtc(), row.Open, row.High, row.Low, row.Close, row.Volume, share, timeframe) is { } valid &&
                    valid.OpenTime >= pageStart && valid.OpenTime < end
                        ? valid
                        : throw new MarketScanException(MarketScanErrorCodes.InvalidProviderData, "T-Invest returned an invalid, misaligned or non-exchange candle.");

                if (!TInvestSessionGrid.IsWeekend(candle.OpenTime))
                {
                    candles[candle.OpenTime] = candle;
                }
            }

            start = end;
        }

        return [.. candles.Values];
    }

    public Task<IReadOnlyList<ScanQuoteInternal>> Quotes(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ScanQuoteInternal>>([]);
    }

    public async IAsyncEnumerable<ScanCandleUpdateInternal> Stream(
        IReadOnlyList<InstrumentSymbol> symbols,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (symbols.Count == 0 || symbols.Count > MaxSymbolsPerStream)
        {
            throw new ArgumentException($"A T-Invest scan stream carries 1 to {MaxSymbolsPerStream} shares.", nameof(symbols));
        }

        TInvestToken token = await marketData.Token(cancellationToken);
        var byUid = new Dictionary<string, TInvestShare>(StringComparer.Ordinal);

        foreach (InstrumentSymbol symbol in symbols.Distinct())
        {
            TInvestShare share = await Share(token, symbol, cancellationToken);
            byUid[share.Uid] = share;
        }

        await budget.Subscriptions.Take(byUid.Count, cancellationToken);

        using TInvestDuplexStream<MarketDataRequest, MarketDataResponse> stream = await transport.Duplex(
            token,
            TInvestMethodGroup.MarketData,
            (invoker, callOptions) => new MarketDataStreamService.MarketDataStreamServiceClient(invoker).MarketDataStream(callOptions),
            cancellationToken);
        var subscribe = new SubscribeCandlesRequest
        {
            SubscriptionAction = SubscriptionAction.Subscribe,
            WaitingClose = true,
            CandleSourceType = GetCandlesRequest.Types.CandleSource.Exchange
        };
        subscribe.Instruments.AddRange(byUid.Keys.Select(uid => new CandleInstrument { InstrumentId = uid, Interval = SubscriptionInterval.OneMinute }));
        await stream.Send(
            new MarketDataRequest { PingSettings = new PingDelaySettings { PingDelayMs = (int)options.Value.Scanner.PingInterval.TotalMilliseconds } },
            cancellationToken);
        await stream.Send(new MarketDataRequest { SubscribeCandlesRequest = subscribe }, cancellationToken);

        await foreach (MarketDataResponse message in stream.Read(cancellationToken))
        {
            if (message.SubscribeCandlesResponse is { } answer)
            {
                int refused = answer.CandlesSubscriptions.Count(item => item.SubscriptionStatus != SubscriptionStatus.Success);

                if (refused > 0)
                {
                    throw new MarketScanException(MarketScanErrorCodes.ProviderUnavailable, $"T-Invest refused {refused} of the scanner's candle subscriptions.");
                }

                yield return ScanCandleUpdateInternal.Alive;
            }
            else if (message.Ping is not null)
            {
                yield return ScanCandleUpdateInternal.Alive;
            }
            else if (message.Candle is { } update && Closed(update, byUid) is { } closed)
            {
                yield return closed;
            }
        }
    }

    /// <summary>
    ///     A closed minute of a share of the stream; an update of another share, interval or source, a weekend minute or an invalid
    ///     candle is dropped, because one bad message must not end the stream of every share on it.
    /// </summary>
    private static ScanCandleUpdateInternal? Closed(Candle update, IReadOnlyDictionary<string, TInvestShare> byUid)
    {
        if (update.Interval != SubscriptionInterval.OneMinute || update.Time is null ||
            update.CandleSourceType is not (CandleSource.Exchange or CandleSource.Unspecified) ||
            !byUid.TryGetValue(update.InstrumentUid, out TInvestShare? share) ||
            TInvestScanCandles.From(update.Time.ToUtc(), update.Open, update.High, update.Low, update.Close, update.Volume, share, TInvestScanCandles.Minute) is not
                { } candle || TInvestSessionGrid.IsWeekend(candle.OpenTime))
        {
            return null;
        }

        return new ScanCandleUpdateInternal(share.Symbol.Symbol.Value, candle, Closed: true);
    }

    private bool IsScanned(Share share)
    {
        return share.ClassCode == TInvestSymbol.MainBoard && string.Equals(share.Currency, "rub", StringComparison.OrdinalIgnoreCase) &&
            share.ApiTradeAvailableFlag && share.BuyAvailableFlag && share.SellAvailableFlag && share.Lot > 0 && share.MinPriceIncrement?.ToDecimal() > 0m &&
            (options.Value.Scanner.QualifiedInvestor || !share.ForQualInvestorFlag);
    }

    private async Task<TInvestShare> Share(TInvestToken token, InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        if (_shares.TryGetValue(symbol.Value, out TInvestShare? known))
        {
            return known;
        }

        if (!TInvestSymbol.TryParse(symbol.Value, out TInvestSymbol? parsed))
        {
            throw new MarketScanException(MarketScanErrorCodes.InvalidProviderData, "The symbol is not a T-Invest share.");
        }

        try
        {
            return _shares[symbol.Value] = await shares.Get(token, parsed, cancellationToken);
        }
        catch (InstrumentRulesException exception)
        {
            throw new MarketScanException(MarketScanErrorCodes.ProviderUnavailable, $"The T-Invest share could not be read ({exception.Code}).");
        }
    }

    private async Task<T> Execute<T>(
        string request,
        TInvestToken token,
        TInvestMethodGroup group,
        Func<CallInvoker, CallOptions, AsyncUnaryCall<T>> call,
        CancellationToken cancellationToken)
    {
        await budget.Requests.Take(units: 1, cancellationToken);

        try
        {
            return await transport.Unary(token, group, call, cancellationToken);
        }
        catch (TInvestRpcException error) when (error.Kind == TInvestFailureKind.RateLimited)
        {
            budget.Requests.Pause(options.Value.RateLimitDefaultReset);

            throw new MarketScanException(MarketScanErrorCodes.RequestsPaused, $"T-Invest reported the limit exhausted for the scanner's {request} request.");
        }
        catch (TInvestRpcException error) when (error.Kind is TInvestFailureKind.Unauthenticated or TInvestFailureKind.PermissionDenied)
        {
            throw new MarketScanException(MarketScanErrorCodes.ProviderUnavailable, $"T-Invest refused the scanner's token for its {request} request.");
        }
        catch (TInvestRpcException error)
        {
            throw new MarketScanException(MarketScanErrorCodes.ProviderUnavailable, $"The scanner's T-Invest {request} request failed ({error.Kind}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MarketScanException(MarketScanErrorCodes.ProviderUnavailable, $"The scanner's T-Invest {request} request timed out.");
        }
    }
}