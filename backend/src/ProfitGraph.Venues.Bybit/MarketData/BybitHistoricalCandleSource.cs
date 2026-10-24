using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using CryptoClients.Net.Interfaces;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;

using Microsoft.Extensions.Logging;

using Polly.CircuitBreaker;
using Polly.Timeout;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     Public Spot klines of Bybit (Profit Fox Designer ADR 0017): backward paging with an exclusive end, discovery of the
///     first candle by binary search over "is there a candle before t", and strict checks of every page.
/// </summary>
internal sealed class BybitHistoricalCandleSource(HttpClient httpClient, ILoggerFactory loggerFactory, BybitResilienceExecutor resilience, BybitOptions options)
    : IHistoricalCandleSource, IDisposable
{
    private const int PageSize = 1000;

    private readonly IExchangeRestClient _client = BybitRestClientFactory.Create(httpClient, loggerFactory, options.AttemptTimeout);

    public VenueId Venue => BybitVenue.VenueId;

    public string Source => "bybit";

    public void Dispose()
    {
        _client.Bybit.Dispose();
    }

    public async Task<DateTimeOffset?> FindFirst(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset to, CancellationToken cancellationToken)
    {
        long count = (to.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / timeframe.Duration.Ticks;

        if (count <= 0 || (await Page(symbol, timeframe, DateTimeOffset.UnixEpoch, to, limit: 1, cancellationToken)).Count == 0)
        {
            return null;
        }

        long low = 0;
        long high = count - 1;

        while (low < high)
        {
            long middle = low + (high - low) / 2;
            DateTimeOffset end = DateTimeOffset.UnixEpoch.AddTicks((middle + 1) * timeframe.Duration.Ticks);

            if ((await Page(symbol, timeframe, DateTimeOffset.UnixEpoch, end, limit: 1, cancellationToken)).Count == 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        DateTimeOffset first = DateTimeOffset.UnixEpoch.AddTicks(low * timeframe.Duration.Ticks);
        IReadOnlyList<Candle> found = await Page(symbol, timeframe, first, first + timeframe.Duration, limit: 1, cancellationToken);

        return found.Count == 1
            ? found[0].OpenTime
            : throw Failure(HistoricalDataErrorCodes.InvalidProviderData, "Bybit history discovery was inconsistent.", first, to);
    }

    public async Task<IReadOnlyList<Candle>> Get(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var candles = new Dictionary<DateTimeOffset, Candle>();
        DateTimeOffset end = to;

        while (end > from)
        {
            IReadOnlyList<Candle> page = await Page(symbol, timeframe, from, end, PageSize, cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            DateTimeOffset oldest = page.Min(candle => candle.OpenTime);

            if (oldest >= end)
            {
                throw Failure(HistoricalDataErrorCodes.InvalidProviderData, "Bybit pagination did not progress.", from, to);
            }

            foreach (Candle candle in page)
            {
                if (candles.TryGetValue(candle.OpenTime, out Candle? previous) && previous != candle)
                {
                    throw Failure(
                        HistoricalDataErrorCodes.Conflict,
                        "Bybit returned conflicting duplicate candles.",
                        candle.OpenTime,
                        candle.OpenTime + timeframe.Duration);
                }

                candles[candle.OpenTime] = candle;
            }

            end = oldest;
        }

        return [.. candles.Values.OrderBy(candle => candle.OpenTime)];
    }

    private async Task<IReadOnlyList<Candle>> Page(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            BybitResponse<BybitKline> data = await resilience.ExecuteAsync(
                async token =>
                {
                    HttpResult<BybitResponse<BybitKline>> result = await _client.Bybit.V5Api.ExchangeData.GetKlinesAsync(
                        Category.Spot,
                        symbol.Value,
                        Interval(timeframe),
                        from.UtcDateTime,
                        to.AddMilliseconds(-1).UtcDateTime,
                        limit,
                        token);
                    token.ThrowIfCancellationRequested();

                    return result.Success ? result.Data : throw new BybitApiException(result);
                },
                cancellationToken);

            if (data.Category != Category.Spot || data.Symbol != symbol.Value || data.List is null)
            {
                throw Failure(HistoricalDataErrorCodes.InvalidProviderData, "Bybit returned another symbol or market, or no candle list.", from, to);
            }

            Candle[] candles =
            [
                .. data.List.Select(row => new Candle(
                    new DateTimeOffset(DateTime.SpecifyKind(row.StartTime, DateTimeKind.Utc)),
                    row.OpenPrice,
                    row.HighPrice,
                    row.LowPrice,
                    row.ClosePrice,
                    row.Volume))
            ];

            if (candles.Length > limit || candles.Any(candle => candle.OpenTime < from || candle.OpenTime >= to || !CandleValidator.IsValid(candle, timeframe)))
            {
                throw Failure(HistoricalDataErrorCodes.InvalidProviderData, "Bybit returned an invalid or out-of-range page.", from, to);
            }

            return candles;
        }
        catch (BybitPayloadException exception)
        {
            throw Failure(HistoricalDataErrorCodes.InvalidProviderData, exception.Message, from, to);
        }
        catch (BybitApiException exception)
        {
            string code = exception.IsInvalidRequest ? HistoricalDataErrorCodes.InvalidRequest
                : exception.ErrorType == ErrorType.DeserializationFailed ? HistoricalDataErrorCodes.InvalidProviderData
                : HistoricalDataErrorCodes.ProviderUnavailable;

            throw Failure(code, $"Bybit rejected the history request (code {exception.Code?.ToString() ?? "unknown"}): {exception.Message}", from, to);
        }
        catch (Exception exception) when (exception is TimeoutRejectedException or BrokenCircuitException or HttpRequestException)
        {
            throw Failure(HistoricalDataErrorCodes.ProviderUnavailable, "The Bybit request failed or timed out.", from, to);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure(HistoricalDataErrorCodes.ProviderUnavailable, "The Bybit request timed out.", from, to);
        }
    }

    private static KlineInterval Interval(Timeframe timeframe)
    {
        return timeframe.Key switch
        {
            "1m" => KlineInterval.OneMinute,
            "3m" => KlineInterval.ThreeMinutes,
            "5m" => KlineInterval.FiveMinutes,
            "15m" => KlineInterval.FifteenMinutes,
            "30m" => KlineInterval.ThirtyMinutes,
            "1h" => KlineInterval.OneHour,
            "2h" => KlineInterval.TwoHours,
            "4h" => KlineInterval.FourHours,
            "6h" => KlineInterval.SixHours,
            "12h" => KlineInterval.TwelveHours,
            "1d" => KlineInterval.OneDay,
            _ => throw new ArgumentException("Unsupported timeframe.", nameof(timeframe))
        };
    }

    private static HistoricalDataException Failure(string code, string message, DateTimeOffset from, DateTimeOffset to)
    {
        return new HistoricalDataException(code, message, [new CandleRangeInternal(from, to)]);
    }
}