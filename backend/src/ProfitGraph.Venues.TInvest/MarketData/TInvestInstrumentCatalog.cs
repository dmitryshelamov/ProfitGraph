using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     Main-board MOEX shares that API trading allows whose ticker or name matches the query
///     (<c>InstrumentsService/FindInstrument</c>, ADR 0023), read with the caller's T-Invest credential. A query that spells
///     one share (<c>GAZP</c> or <c>TQBR:GAZP</c>) also reads it with <c>ShareBy</c> for its brand's logo, sector and country;
///     a share whose brand cannot be read is listed without them.
/// </summary>
internal sealed class TInvestInstrumentCatalog(TInvestTransport transport, TInvestShares shares) : IInstrumentCatalog
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

        // A whole symbol is searched by its ticker, as the bot page asks for the instrument it trades.
        string? exact = TInvestSymbol.TryParse(query, out TInvestSymbol? whole) ? whole.Ticker : null;

        FindInstrumentResponse response;

        try
        {
            response = await transport.Unary(
                token,
                TInvestMethodGroup.Instruments,
                (invoker, options) => new InstrumentsService.InstrumentsServiceClient(invoker).FindInstrumentAsync(
                    new FindInstrumentRequest { Query = exact ?? query, InstrumentKind = InstrumentType.Share, ApiTradeAvailableFlag = true },
                    options),
                cancellationToken);
        }
        catch (TInvestRpcException)
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.ProviderUnavailable, "T-Invest is unavailable; try again later.");
        }

        IReadOnlyList<TInvestSymbol> found =
        [
            .. response.Instruments
                .Where(instrument => instrument.ClassCode == TInvestSymbol.MainBoard && instrument.ApiTradeAvailableFlag && instrument.Lot > 0)
                .Select(instrument => TInvestSymbol.TryParse(instrument.Ticker, out TInvestSymbol? symbol) ? symbol : null)
                .OfType<TInvestSymbol>()
                .DistinctBy(symbol => symbol.Ticker)
                .Take(limit)
        ];
        var instruments = new List<InstrumentInternal>();

        foreach (TInvestSymbol symbol in found)
        {
            InstrumentAppearanceInternal? appearance = symbol.Ticker == exact ? await Appearance(token, symbol, cancellationToken) : null;
            instruments.Add(new InstrumentInternal(TInvestVenue.VenueId.Value, symbol.Symbol.Value, symbol.Ticker, "RUB", appearance));
        }

        return instruments;
    }

    private async Task<InstrumentAppearanceInternal?> Appearance(TInvestToken token, TInvestSymbol symbol, CancellationToken cancellationToken)
    {
        try
        {
            return TInvestShareAppearance.From(await shares.Get(token, symbol, cancellationToken));
        }
        catch (InstrumentRulesException)
        {
            return null;
        }
    }
}