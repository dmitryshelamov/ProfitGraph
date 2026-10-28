using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     Reads a share by ticker and class code (<c>InstrumentsService/ShareBy</c>) and accepts it only as a rouble share on
///     the main board that API trading allows, with a positive lot and price increment (ADR 0023). A share read is reused
///     for an hour.
/// </summary>
internal sealed class TInvestShares(TInvestTransport transport, IOptions<TInvestOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, (TInvestShare Share, DateTimeOffset ReadAt)> _read =
        new ConcurrentDictionary<string, (TInvestShare Share, DateTimeOffset ReadAt)>();

    public async Task<TInvestShare> Get(TInvestToken token, TInvestSymbol symbol, CancellationToken cancellationToken)
    {
        string key = symbol.Symbol.Value;

        if (_read.TryGetValue(key, out (TInvestShare Share, DateTimeOffset ReadAt) cached) && clock.GetUtcNow() - cached.ReadAt < options.Value.ShareReuse)
        {
            return cached.Share;
        }

        ShareResponse response;

        try
        {
            response = await transport.Unary(
                token,
                TInvestMethodGroup.Instruments,
                (invoker, options) => new InstrumentsService.InstrumentsServiceClient(invoker).ShareByAsync(
                    new InstrumentRequest { IdType = InstrumentIdType.Ticker, ClassCode = symbol.ClassCode, Id = symbol.Ticker },
                    options),
                cancellationToken);
        }
        catch (TInvestRpcException error) when (error.Kind is TInvestFailureKind.NotFound or TInvestFailureKind.Rejected)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidRequest, "T-Invest has no such share.");
        }
        catch (TInvestRpcException)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.ProviderUnavailable, "T-Invest is unavailable; try again later.");
        }

        TInvestShare share = Accept(response.Instrument, symbol);
        _read[key] = (share, clock.GetUtcNow());

        return share;
    }

    private static TInvestShare Accept(Share? share, TInvestSymbol symbol)
    {
        if (share is null || share.Ticker != symbol.Ticker || share.ClassCode != symbol.ClassCode)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidProviderData, "T-Invest returned another share.");
        }

        decimal increment = share.MinPriceIncrement?.ToDecimal() ?? 0m;

        if (!string.Equals(share.Currency, "rub", StringComparison.OrdinalIgnoreCase) || !share.ApiTradeAvailableFlag || share.Lot <= 0 || increment <= 0m)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidRequest, "The share is not a rouble share that API trading allows.");
        }

        return new TInvestShare(
            symbol,
            share.Uid,
            share.Name,
            share.Lot,
            increment,
            share.First1MinCandleDate?.ToUtc(),
            share.First1DayCandleDate?.ToUtc(),
            share.Exchange,
            share.Brand is null ? null : new TInvestBrand(share.Brand.LogoName, share.Brand.LogoBaseColor),
            share.Sector,
            share.CountryOfRiskName);
    }
}