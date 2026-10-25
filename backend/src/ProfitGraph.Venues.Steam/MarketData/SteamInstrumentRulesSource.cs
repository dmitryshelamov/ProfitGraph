using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     The rules of every Steam item (ADR 0022): whole items, a price tick of the wallet currency's minor unit and the
///     smallest buyer price a seller can reach under Steam's default fees. They follow from the symbol, so nothing is asked.
/// </summary>
internal sealed class SteamInstrumentRulesSource : IInstrumentRulesSource
{
    private static readonly SellerFeeSchedule DefaultFees = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);

    public VenueId Venue => SteamVenue.VenueId;

    public Task<InstrumentRulesDefinitionInternal> Get(InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        if (!SteamInstrument.TryParse(symbol.Value, out SteamInstrument? instrument))
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidRequest, "The symbol is not a Steam item.");
        }

        decimal tick = instrument.Currency.MinorUnit;
        int precision = instrument.Currency.Precision;
        var rules = new InstrumentRules(
            quantityStep: 1m,
            tick,
            minQuantity: 1m,
            DefaultFees.BuyerPrice(tick, precision),
            basePrecision: 0,
            precision);

        return Task.FromResult(
            new InstrumentRulesDefinitionInternal(
                SteamVenue.VenueId.Value,
                "steam",
                "community-market",
                symbol.Value,
                instrument.MarketHashName,
                instrument.Currency.Code,
                BasePrecisionIncrement: 1m,
                tick,
                rules));
    }
}