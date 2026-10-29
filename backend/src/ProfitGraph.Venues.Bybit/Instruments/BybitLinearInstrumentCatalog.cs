using CryptoClients.Net.Interfaces;

using Microsoft.Extensions.Logging;

using Polly.CircuitBreaker;
using Polly.Timeout;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Instruments;

/// <summary>
///     Bybit's tradable USDT perpetuals (ADR 0061) for picking a pair, read page by page and reused for
///     <see cref="BybitOptions.InstrumentListLifetime" /> in their own list.
/// </summary>
internal sealed class BybitLinearInstrumentCatalog(
    HttpClient httpClient,
    ILoggerFactory loggerFactory,
    BybitResilienceExecutor resilience,
    BybitOptions options,
    BybitInstrumentList list) : IInstrumentCatalog, IDisposable
{
    private readonly IExchangeRestClient _client = BybitRestClientFactory.Create(httpClient, loggerFactory, options.AttemptTimeout);

    public VenueId Venue => BybitMarket.Linear.Venue;

    public void Dispose()
    {
        _client.Bybit.Dispose();
    }

    public async Task<IReadOnlyList<InstrumentInternal>> Search(string query, int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<InstrumentInternal> instruments = await list.Get(Load, cancellationToken);
        string normalized = query.Replace("/", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        return
        [
            .. instruments
                .Where(instrument => instrument.Symbol.Contains(normalized, StringComparison.Ordinal) ||
                    instrument.BaseAsset.Contains(normalized, StringComparison.Ordinal))
                .OrderBy(instrument => instrument.Symbol == normalized ? 0 : instrument.Symbol.StartsWith(normalized, StringComparison.Ordinal) ? 1 : 2)
                .ThenBy(instrument => instrument.Symbol, StringComparer.Ordinal)
                .Take(limit)
        ];
    }

    private async Task<IReadOnlyList<InstrumentInternal>> Load(CancellationToken cancellationToken)
    {
        try
        {
            return
            [
                .. (await BybitLinearInstruments.All(_client, resilience, take: null, cancellationToken))
                    .Where(BybitLinearInstruments.IsTradable)
                    .Select(instrument => new InstrumentInternal(
                        BybitMarket.Linear.Venue.Value,
                        instrument.Name,
                        instrument.BaseAsset,
                        instrument.QuoteAsset,
                        BybitInstrumentCatalog.Appearance(instrument.BaseAsset)))
                    .DistinctBy(instrument => instrument.Symbol)
                    .OrderBy(instrument => instrument.Symbol, StringComparer.Ordinal)
            ];
        }
        catch (BybitPayloadException exception)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidProviderData, exception.Message);
        }
        catch (BybitApiException exception)
        {
            throw new InstrumentRulesException(
                InstrumentRulesErrorCodes.ProviderUnavailable,
                $"Bybit rejected the linear instrument list request (code {exception.Code?.ToString() ?? "unknown"}): {exception.Message}");
        }
        catch (Exception exception) when (exception is TimeoutRejectedException or BrokenCircuitException or HttpRequestException)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.ProviderUnavailable, "The Bybit linear instrument list request failed or timed out.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.ProviderUnavailable, "The Bybit linear instrument list request timed out.");
        }
    }
}
