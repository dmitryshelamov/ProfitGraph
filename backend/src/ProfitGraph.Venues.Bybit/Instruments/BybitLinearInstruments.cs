using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using CryptoClients.Net.Interfaces;

using CryptoExchange.Net.Objects;

using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Instruments;

/// <summary>
///     Bybit's USDT perpetuals (ADR 0061): linear contracts that settle in USDT, are quoted in it and trade now. Bybit pages the
///     linear instrument list; every page is read.
/// </summary>
internal static class BybitLinearInstruments
{
    private const int PageSize = 1000;
    private const int MaxPages = 20;
    private const string SettleAsset = "USDT";

    public static bool IsTradable(BybitLinearInverseSymbol instrument)
    {
        return instrument.Status == SymbolStatus.Trading && instrument.ContractType == ContractTypeV5.LinearPerpetual &&
            instrument.SettleAsset == SettleAsset && instrument.QuoteAsset == SettleAsset && IsConsistent(instrument);
    }

    public static bool IsConsistent(BybitLinearInverseSymbol instrument)
    {
        return !string.IsNullOrWhiteSpace(instrument.BaseAsset) && !string.IsNullOrWhiteSpace(instrument.QuoteAsset) &&
            instrument.BaseAsset != instrument.QuoteAsset && instrument.BaseAsset + instrument.QuoteAsset == instrument.Name;
    }

    /// <summary>
    ///     Every linear instrument, page after page, each request under the module's resilience; <paramref name="take" /> runs
    ///     before each attempt, such as the scanner's request budget.
    /// </summary>
    public static async Task<IReadOnlyList<BybitLinearInverseSymbol>> All(
        IExchangeRestClient client,
        BybitResilienceExecutor resilience,
        Func<CancellationToken, Task>? take,
        CancellationToken cancellationToken)
    {
        List<BybitLinearInverseSymbol> instruments = [];
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            string? current = cursor;
            BybitResponse<BybitLinearInverseSymbol> data = await resilience.ExecuteAsync(
                async token =>
                {
                    if (take is not null)
                    {
                        await take(token);
                    }

                    HttpResult<BybitResponse<BybitLinearInverseSymbol>> result = await client.Bybit.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(
                        Category.Linear,
                        limit: PageSize,
                        cursor: current,
                        ct: token);
                    token.ThrowIfCancellationRequested();

                    return result.Success ? result.Data : throw new BybitApiException(result);
                },
                cancellationToken);

            if (data.Category != Category.Linear || data.List is null)
            {
                throw new BybitPayloadException("Bybit returned no linear instrument list.");
            }

            instruments.AddRange(data.List);
            cursor = string.IsNullOrEmpty(data.NextPageCursor) ? null : data.NextPageCursor;

            if (cursor is null)
            {
                return instruments;
            }
        }

        throw new BybitPayloadException("Bybit's linear instrument list did not end.");
    }
}
