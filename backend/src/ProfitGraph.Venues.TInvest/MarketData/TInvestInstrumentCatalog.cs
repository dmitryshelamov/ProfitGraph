using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     Main-board MOEX shares that API trading allows whose ticker or name matches the query
///     (<c>InstrumentsService/FindInstrument</c>, ADR 0023), read with the caller's T-Invest credential.
/// </summary>
internal sealed class TInvestInstrumentCatalog(TInvestTransport transport) : IInstrumentCatalog
{
    public VenueId Venue => TInvestVenue.VenueId;

    public async Task<IReadOnlyList<InstrumentInternal>> Search(string query, int limit, CancellationToken cancellationToken)
    {
        TInvestToken token = TInvestMarketDataToken.Current()
            ?? throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidRequest, "T-Invest shares are searched with your T-Invest credential; choose one.");

        if (query.Length == 0)
        {
            return [];
        }

        FindInstrumentResponse response;

        try
        {
            response = await transport.Unary(
                token,
                TInvestMethodGroup.Instruments,
                (invoker, options) => new InstrumentsService.InstrumentsServiceClient(invoker).FindInstrumentAsync(
                    new FindInstrumentRequest { Query = query, InstrumentKind = InstrumentType.Share, ApiTradeAvailableFlag = true },
                    options),
                cancellationToken);
        }
        catch (TInvestRpcException)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.ProviderUnavailable, "T-Invest is unavailable; try again later.");
        }

        return
        [
            .. response.Instruments
                .Where(instrument => instrument.ClassCode == TInvestSymbol.MainBoard && instrument.ApiTradeAvailableFlag && instrument.Lot > 0)
                .Select(instrument => TInvestSymbol.TryParse(instrument.Ticker, out TInvestSymbol? symbol) ? symbol : null)
                .OfType<TInvestSymbol>()
                .DistinctBy(symbol => symbol.Ticker)
                .Take(limit)
                .Select(symbol => new InstrumentInternal(TInvestVenue.VenueId.Value, symbol.Symbol.Value, symbol.Ticker, "RUB"))
        ];
    }
}