using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using CryptoClients.Net.Interfaces;

using CryptoExchange.Net.Objects;

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
///     Bybit's tradable Spot pairs from one public request (Spot instruments are not paged), reused for
///     <see cref="BybitOptions.InstrumentListLifetime" />. Pairs that are not trading or whose assets do not spell the
///     symbol are left out.
/// </summary>
internal sealed class BybitInstrumentCatalog(
    HttpClient httpClient,
    ILoggerFactory loggerFactory,
    BybitResilienceExecutor resilience,
    BybitOptions options,
    BybitInstrumentList list) : IInstrumentCatalog, IDisposable
{
    private readonly IExchangeRestClient _client = BybitRestClientFactory.Create(httpClient, loggerFactory, options.AttemptTimeout);

    public VenueId Venue => BybitVenue.VenueId;

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
                    instrument.BaseAsset.Contains(normalized, StringComparison.Ordinal) ||
                    instrument.QuoteAsset.Contains(normalized, StringComparison.Ordinal))
                .OrderBy(instrument => instrument.Symbol == normalized ? 0 : instrument.Symbol.StartsWith(normalized, StringComparison.Ordinal) ? 1 : 2)
                .ThenBy(instrument => instrument.Symbol, StringComparer.Ordinal)
                .Take(limit)
        ];
    }

    internal static bool IsConsistent(BybitSpotSymbol instrument)
    {
        return !string.IsNullOrWhiteSpace(instrument.BaseAsset) && !string.IsNullOrWhiteSpace(instrument.QuoteAsset) &&
            instrument.BaseAsset != instrument.QuoteAsset && instrument.BaseAsset + instrument.QuoteAsset == instrument.Name;
    }

    /// <summary>
    ///     Bybit publishes no coin icons without a key, so the base coin's picture comes from CoinCap's public icon set by
    ///     ticker; a coin it lacks shows the plate on the page.
    /// </summary>
    private static InstrumentAppearanceInternal? Appearance(string baseAsset)
    {
        return baseAsset.Length <= 12 && baseAsset.All(char.IsAsciiLetterOrDigit)
            ? new InstrumentAppearanceInternal(
                $"https://assets.coincap.io/assets/icons/{baseAsset.ToLowerInvariant()}@2x.png",
                Kind: null,
                NameColor: null,
                BackgroundColor: null,
                [])
            : null;
    }

    private async Task<IReadOnlyList<InstrumentInternal>> Load(CancellationToken cancellationToken)
    {
        try
        {
            BybitResponse<BybitSpotSymbol> data = await resilience.ExecuteAsync(
                async token =>
                {
                    HttpResult<BybitResponse<BybitSpotSymbol>> result = await _client.Bybit.V5Api.ExchangeData.GetSpotSymbolsAsync(ct: token);
                    token.ThrowIfCancellationRequested();

                    return result.Success ? result.Data : throw new BybitApiException(result);
                },
                cancellationToken);

            if (data.Category != Category.Spot || data.List is null)
            {
                throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidProviderData, "Bybit returned no Spot instrument list.");
            }

            return
            [
                .. data.List
                    .Where(instrument => instrument.Status == SymbolStatus.Trading && IsConsistent(instrument))
                    .Select(instrument => new InstrumentInternal(
                        BybitVenue.VenueId.Value,
                        instrument.Name,
                        instrument.BaseAsset,
                        instrument.QuoteAsset,
                        Appearance(instrument.BaseAsset)))
                    .DistinctBy(instrument => instrument.Symbol)
                    .OrderBy(instrument => instrument.Symbol, StringComparer.Ordinal)
            ];
        }
        catch (BybitApiException exception)
        {
            throw new InstrumentRulesException(
                InstrumentRulesErrorCodes.ProviderUnavailable,
                $"Bybit rejected the instrument list request (code {exception.Code?.ToString() ?? "unknown"}): {exception.Message}");
        }
        catch (Exception exception) when (exception is TimeoutRejectedException or BrokenCircuitException or HttpRequestException)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.ProviderUnavailable, "The Bybit instrument list request failed or timed out.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.ProviderUnavailable, "The Bybit instrument list request timed out.");
        }
    }
}