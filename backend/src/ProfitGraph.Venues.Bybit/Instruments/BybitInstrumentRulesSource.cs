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
///     Current public Spot rules of one Bybit instrument (Profit Fox Designer ADR 0021): the base precision is the
///     quantity step, the tick size the price step and the minimum order value the minimum notional; Bybit Spot has no
///     separate minimum quantity for a market order by value, so it is zero.
/// </summary>
internal sealed class BybitInstrumentRulesSource(HttpClient httpClient, ILoggerFactory loggerFactory, BybitResilienceExecutor resilience, BybitOptions options)
    : IInstrumentRulesSource, IDisposable
{
    private const int MaxPrecision = 28;

    private readonly IExchangeRestClient _client = BybitRestClientFactory.Create(httpClient, loggerFactory, options.AttemptTimeout);

    public VenueId Venue => BybitVenue.VenueId;

    public void Dispose()
    {
        _client.Bybit.Dispose();
    }

    public async Task<InstrumentRulesDefinitionInternal> Get(InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        try
        {
            BybitResponse<BybitSpotSymbol> data = await resilience.ExecuteAsync(
                async token =>
                {
                    HttpResult<BybitResponse<BybitSpotSymbol>> result = await _client.Bybit.V5Api.ExchangeData.GetSpotSymbolsAsync(symbol.Value, token);
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

    private static InstrumentRulesDefinitionInternal Definition(string symbol, BybitResponse<BybitSpotSymbol> data)
    {
        if (data.Category != Category.Spot || data.List is null || data.List.Length > 1)
        {
            throw Failure(InstrumentRulesErrorCodes.InvalidProviderData, "Bybit must return at most one Spot instrument.");
        }

        BybitSpotSymbol instrument = data.List.SingleOrDefault()
            ?? throw Failure(InstrumentRulesErrorCodes.InvalidRequest, $"Bybit has no Spot instrument '{symbol}'.");

        if (instrument.Name == symbol && instrument.Status != SymbolStatus.Trading)
        {
            throw Failure(InstrumentRulesErrorCodes.InvalidRequest, $"Spot instrument '{symbol}' is not trading (status {instrument.Status}).");
        }

        if (instrument.Name != symbol || !BybitInstrumentCatalog.IsConsistent(instrument) || instrument.LotSizeFilter is null ||
            instrument.PriceFilter is null)
        {
            throw Failure(InstrumentRulesErrorCodes.InvalidProviderData, "Bybit returned inconsistent or incomplete instrument metadata.");
        }

        decimal baseIncrement = instrument.LotSizeFilter.BasePrecision;
        decimal quoteIncrement = instrument.LotSizeFilter.QuotePrecision;
        decimal tick = instrument.PriceFilter.TickSize;
        decimal minimum = instrument.LotSizeFilter.MinOrderValue;

        if (tick <= 0m || minimum <= 0m)
        {
            throw Failure(InstrumentRulesErrorCodes.InvalidProviderData, "Bybit returned a non-positive price tick or minimum order value.");
        }

        var rules = new InstrumentRules(baseIncrement, tick, minQuantity: 0m, minimum, Precision(baseIncrement), Precision(quoteIncrement));

        return new InstrumentRulesDefinitionInternal(
            BybitVenue.VenueId.Value,
            "bybit",
            "spot",
            symbol,
            instrument.BaseAsset,
            instrument.QuoteAsset,
            baseIncrement,
            quoteIncrement,
            rules);
    }

    /// <summary>
    ///     The number of decimal places of an increment that must be an exact power of ten from 1 to 10^-28.
    /// </summary>
    private static int Precision(decimal increment)
    {
        var candidate = 1m;

        for (var precision = 0; precision <= MaxPrecision; precision++)
        {
            if (increment == candidate)
            {
                return precision;
            }

            candidate /= 10m;
        }

        throw Failure(InstrumentRulesErrorCodes.InvalidProviderData, "Asset precision increments must be exact powers of ten from 1 to 10^-28.");
    }

    private static InstrumentRulesException Failure(string code, string message)
    {
        return new InstrumentRulesException(code, message);
    }
}