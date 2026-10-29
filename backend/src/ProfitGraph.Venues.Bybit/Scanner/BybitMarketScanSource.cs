using System.Runtime.CompilerServices;

using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using CryptoClients.Net.Interfaces;

using CryptoExchange.Net.Objects;

using Microsoft.Extensions.Logging;

using Polly.CircuitBreaker;
using Polly.Timeout;

using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Venues.Bybit.Instruments;
using ProfitGraph.Venues.Bybit.LiveData;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Scanner;

/// <summary>
///     Bybit Spot public market data for the market scanner (ADR 0059): trading pairs quoted in
///     <see cref="BybitOptions.ScannerQuoteAsset" />, klines with turnover read backwards in pages of 1000, every pair's best bid
///     and ask from one ticker request, and 1-minute kline topics, up to 100 on one public connection. Each REST attempt takes a
///     turn of <see cref="BybitScanRequestBudget" />; a 403 starts its ban pause. Bybit's Spot instrument list carries no listing
///     time, so <see cref="ScanInstrumentInternal.ListedAt" /> stays null.
/// </summary>
internal sealed class BybitMarketScanSource(
    HttpClient klineClient,
    HttpClient quoteClient,
    ILoggerFactory loggerFactory,
    BybitResilienceExecutor resilience,
    BybitScanRequestBudget budget,
    IBybitPublicStreamTransport transport,
    BybitOptions options) : IMarketScanSource, IDisposable
{
    private const int PageSize = 1000;
    private const int TopicsPerConnection = 100;

    private readonly IExchangeRestClient _klines = BybitRestClientFactory.Create(klineClient, loggerFactory, options.AttemptTimeout);
    private readonly IExchangeRestClient _quotes = BybitRestClientFactory.Create(quoteClient, loggerFactory, options.AttemptTimeout);

    public VenueId Venue => BybitVenue.VenueId;

    public int MaxSymbolsPerStream => TopicsPerConnection;

    public void Dispose()
    {
        _klines.Bybit.Dispose();
        _quotes.Bybit.Dispose();
    }

    public async Task<IReadOnlyList<ScanInstrumentInternal>> Universe(CancellationToken cancellationToken)
    {
        BybitResponse<BybitSpotSymbol> data = await Execute(
            "instrument list",
            token => _quotes.Bybit.V5Api.ExchangeData.GetSpotSymbolsAsync(ct: token),
            cancellationToken);

        if (data.Category != Category.Spot || data.List is null)
        {
            throw new MarketScanException(MarketScanErrorCodes.InvalidProviderData, "Bybit returned no Spot instrument list.");
        }

        return
        [
            .. data.List
                .Where(instrument => instrument.Status == SymbolStatus.Trading && BybitInstrumentCatalog.IsConsistent(instrument) &&
                    instrument.QuoteAsset == options.ScannerQuoteAsset)
                .Select(instrument => instrument.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(symbol => new ScanInstrumentInternal(symbol, ListedAt: null))
        ];
    }

    public async Task<IReadOnlyList<ScanCandle>> Candles(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var candles = new SortedDictionary<DateTimeOffset, ScanCandle>();
        DateTimeOffset end = to;

        while (end > from)
        {
            BybitResponse<BybitKline> data = await Execute(
                "kline",
                token => _klines.Bybit.V5Api.ExchangeData.GetKlinesAsync(
                    Category.Spot,
                    symbol.Value,
                    BybitHistoricalCandleSource.Interval(timeframe),
                    from.UtcDateTime,
                    end.AddMilliseconds(-1).UtcDateTime,
                    PageSize,
                    token),
                cancellationToken);

            if (data.Category != Category.Spot || data.Symbol != symbol.Value || data.List is null)
            {
                throw new MarketScanException(MarketScanErrorCodes.InvalidProviderData, "Bybit returned another symbol or market, or no candle list.");
            }

            ScanCandle[] page = [.. data.List.Select(Candle)];

            if (page.Length == 0)
            {
                break;
            }

            if (page.Length > PageSize || page.Any(candle => candle.OpenTime < from || candle.OpenTime >= end || !IsValid(candle, timeframe)))
            {
                throw new MarketScanException(MarketScanErrorCodes.InvalidProviderData, "Bybit returned an invalid or out-of-range kline page.");
            }

            foreach (ScanCandle candle in page)
            {
                candles[candle.OpenTime] = candle;
            }

            end = page.Min(candle => candle.OpenTime);
        }

        return [.. candles.Values];
    }

    public async Task<IReadOnlyList<ScanQuoteInternal>> Quotes(CancellationToken cancellationToken)
    {
        BybitResponse<BybitSpotTicker> data = await Execute(
            "ticker",
            token => _quotes.Bybit.V5Api.ExchangeData.GetSpotTickersAsync(ct: token),
            cancellationToken);

        if (data.Category != Category.Spot || data.List is null)
        {
            throw new MarketScanException(MarketScanErrorCodes.InvalidProviderData, "Bybit returned no Spot ticker list.");
        }

        return
        [
            .. data.List
                .DistinctBy(ticker => ticker.Symbol)
                .Select(ticker => new ScanQuoteInternal(ticker.Symbol, Positive(ticker.BestBidPrice), Positive(ticker.BestAskPrice)))
        ];
    }

    public async IAsyncEnumerable<ScanCandleUpdateInternal> Stream(
        IReadOnlyList<InstrumentSymbol> symbols,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (symbols.Count is 0 or > TopicsPerConnection)
        {
            throw new ArgumentException($"A Bybit scan stream carries 1 to {TopicsPerConnection} pairs.", nameof(symbols));
        }

        HashSet<string> names = [.. symbols.Select(symbol => symbol.Value)];

        await foreach (string message in transport.Connect([.. names.Select(BybitScanKlines.Topic)], cancellationToken))
        {
            foreach (ScanCandleUpdateInternal update in BybitScanKlines.Read(message, names))
            {
                yield return update;
            }
        }
    }

    private static ScanCandle Candle(BybitKline row)
    {
        return new ScanCandle(
            new DateTimeOffset(DateTime.SpecifyKind(row.StartTime, DateTimeKind.Utc)),
            row.OpenPrice,
            row.HighPrice,
            row.LowPrice,
            row.ClosePrice,
            row.Volume,
            row.QuoteVolume);
    }

    private static bool IsValid(ScanCandle candle, Timeframe timeframe)
    {
        return timeframe.IsAligned(candle.OpenTime) && candle.Open > 0 && candle.Low > 0 && candle.Close > 0 && candle.Volume >= 0 &&
            candle.Turnover >= 0 && candle.High >= Math.Max(candle.Open, candle.Close) && candle.Low <= Math.Min(candle.Open, candle.Close);
    }

    private static decimal? Positive(decimal? price)
    {
        return price is > 0 ? price : null;
    }

    private async Task<T> Execute<T>(string request, Func<CancellationToken, Task<HttpResult<T>>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await resilience.ExecuteAsync(
                async token =>
                {
                    await budget.Take(token);
                    HttpResult<T> result = await call(token);
                    token.ThrowIfCancellationRequested();

                    return result.Success ? result.Data : throw new BybitApiException(result);
                },
                cancellationToken);
        }
        catch (BybitApiException exception) when (exception.IsForbidden)
        {
            budget.Pause();

            throw new MarketScanException(MarketScanErrorCodes.RequestsPaused, $"Bybit answered 403 to the scanner's {request} request.");
        }
        catch (BybitApiException exception)
        {
            throw new MarketScanException(
                MarketScanErrorCodes.ProviderUnavailable,
                $"Bybit rejected the scanner's {request} request (code {exception.Code?.ToString() ?? "unknown"}).");
        }
        catch (BybitPayloadException exception)
        {
            throw new MarketScanException(MarketScanErrorCodes.InvalidProviderData, exception.Message);
        }
        catch (Exception exception) when (exception is TimeoutRejectedException or BrokenCircuitException or HttpRequestException)
        {
            throw new MarketScanException(MarketScanErrorCodes.ProviderUnavailable, $"The scanner's Bybit {request} request failed or timed out.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MarketScanException(MarketScanErrorCodes.ProviderUnavailable, $"The scanner's Bybit {request} request timed out.");
        }
    }
}