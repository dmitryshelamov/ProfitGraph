using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     A share's rules (ADR 0023): quantities in shares stepping by the lot with one lot at least, the exact price increment,
///     no minimum value, roubles to the kopeck, and fees in roubles on both sides counted exactly. Read with the caller's
///     T-Invest credential.
/// </summary>
internal sealed class TInvestInstrumentRulesSource(TInvestShares shares) : IInstrumentRulesSource
{
    public VenueId Venue => TInvestVenue.VenueId;

    public async Task<InstrumentRulesDefinitionInternal> Get(InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        if (!TInvestSymbol.TryParse(symbol.Value, out TInvestSymbol? parsed))
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidRequest, "The symbol is not a T-Invest share.");
        }

        TInvestShare share = await shares.Get(
            TInvestMarketDataToken.Current()
            ?? throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidRequest, "T-Invest rules are read with your T-Invest credential; choose one."),
            parsed,
            cancellationToken);
        var rules = new InstrumentRules(
            share.Lot,
            share.PriceIncrement,
            share.Lot,
            minNotional: 0m,
            basePrecision: 0,
            quotePrecision: 2,
            FeeAsset.Quote,
            exactQuoteFees: true);

        return new InstrumentRulesDefinitionInternal(
            TInvestVenue.VenueId.Value,
            "tinvest",
            "moex-" + parsed.ClassCode.ToLowerInvariant(),
            symbol.Value,
            parsed.Ticker,
            "RUB",
            BasePrecisionIncrement: 1m,
            QuotePrecisionIncrement: 0.01m,
            rules);
    }
}