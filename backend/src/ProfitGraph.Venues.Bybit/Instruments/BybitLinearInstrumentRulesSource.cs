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
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Instruments;

/// <summary>
///     Current public rules of one Bybit USDT perpetual (ADR 0061): the quantity step and minimum order quantity of its lot filter,
///     its tick size and its minimum notional value. Every fee is paid in USDT and kept exact, as Bybit reports it.
/// </summary>
internal sealed class BybitLinearInstrumentRulesSource(HttpClient httpClient, ILoggerFactory loggerFactory, BybitResilienceExecutor resilience, BybitOptions options)
    : IInstrumentRulesSource, IDisposable
{
    private const int QuotePrecision = 8;

    private readonly IExchangeRestClient _client = BybitRestClientFactory.Create(httpClient, loggerFactory, options.AttemptTimeout);

    public VenueId Venue => BybitMarket.Linear.Venue;

    public void Dispose()
    {
        _client.Bybit.Dispose();
    }

    public async Task<InstrumentRulesDefinitionInternal> Get(InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        try
        {
            BybitResponse<BybitLinearInverseSymbol> data = await resilience.ExecuteAsync(
                async token =>
                {
                    HttpResult<BybitResponse<BybitLinearInverseSymbol>> result =
                        await _client.Bybit.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, symbol.Value, ct: token);
                    token.ThrowIfCancellationRequested();

                    return result.Success ? result.Data : throw new BybitApiException(result);
                },
                cancellationToken);

            return Definition(symbol.Value, data);
        }
        catch (BybitPayloadException exception)
        {
            throw Failure(InstrumentRulesErrorCodes.InvalidProviderData, exception.Message);
        }
        catch (BybitApiException exception)
        {
            string code = exception.IsInvalidRequest ? InstrumentRulesErrorCodes.InvalidRequest
                : exception.ErrorType == ErrorType.DeserializationFailed ? InstrumentRulesErrorCodes.InvalidProviderData
                : InstrumentRulesErrorCodes.ProviderUnavailable;

            throw Failure(code, $"Bybit rejected the instrument request (code {exception.Code?.ToString() ?? "unknown"}): {exception.Message}");
        }
        catch (Exception exception) when (exception is TimeoutRejectedException or BrokenCircuitException or HttpRequestException)
        {
            throw Failure(InstrumentRulesErrorCodes.ProviderUnavailable, "The Bybit instrument request failed or timed out.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure(InstrumentRulesErrorCodes.ProviderUnavailable, "The Bybit instrument request timed out.");
        }
    }

    /// <summary>
    ///     The number of decimal places of a positive step, such as 0.001 (3) or 10 (0).
    /// </summary>
    internal static int Scale(decimal step)
    {
        return (decimal.GetBits(step / 1.0000000000000000000000000000m)[3] >> 16) & 0xFF;
    }

    private static InstrumentRulesDefinitionInternal Definition(string symbol, BybitResponse<BybitLinearInverseSymbol> data)
    {
        if (data.Category != Category.Linear || data.List is null || data.List.Length > 1)
        {
            throw Failure(InstrumentRulesErrorCodes.InvalidProviderData, "Bybit must return at most one linear instrument.");
        }

        BybitLinearInverseSymbol instrument = data.List.SingleOrDefault()
            ?? throw Failure(InstrumentRulesErrorCodes.InvalidRequest, $"Bybit has no linear instrument '{symbol}'.");

        if (instrument.Name == symbol && instrument.Status != SymbolStatus.Trading)
        {
            throw Failure(InstrumentRulesErrorCodes.InvalidRequest, $"Linear instrument '{symbol}' is not trading (status {instrument.Status}).");
        }

        if (instrument.Name != symbol || !BybitLinearInstruments.IsTradable(instrument))
        {
            throw Failure(InstrumentRulesErrorCodes.InvalidRequest, $"'{symbol}' is not a USDT perpetual.");
        }

        if (instrument.LotSizeFilter is not { } lot || instrument.PriceFilter is not { } price || lot.QuantityStep <= 0m || price.TickSize <= 0m ||
            lot.MinOrderQuantity < 0m || lot.MinNotionalValue is not (>= 0m and var minimum))
        {
            throw Failure(InstrumentRulesErrorCodes.InvalidProviderData, "Bybit returned inconsistent or incomplete instrument metadata.");
        }

        int basePrecision = Scale(lot.QuantityStep);
        var rules = new InstrumentRules(
            lot.QuantityStep,
            price.TickSize,
            lot.MinOrderQuantity,
            minimum,
            basePrecision,
            QuotePrecision,
            FeeAsset.Quote,
            exactQuoteFees: true);

        return new InstrumentRulesDefinitionInternal(
            BybitMarket.Linear.Venue.Value,
            "bybit",
            BybitMarket.Linear.Name,
            symbol,
            instrument.BaseAsset,
            instrument.QuoteAsset,
            new decimal(lo: 1, mid: 0, hi: 0, isNegative: false, (byte)basePrecision),
            new decimal(lo: 1, mid: 0, hi: 0, isNegative: false, QuotePrecision),
            rules);
    }

    private static InstrumentRulesException Failure(string code, string message)
    {
        return new InstrumentRulesException(code, message);
    }
}
