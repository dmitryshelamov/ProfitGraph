using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     The rules of every Steam item (ADR 0022): whole items, a price tick of the wallet currency's minor unit times the
///     wallet's currency increment (a whole tenge for KZT) and the smallest buyer price a seller can reach under the wallet's
///     fees. They come from the market-data account's wallet, which must be in the item's currency.
/// </summary>
internal sealed class SteamInstrumentRulesSource(SteamMarketDataAccount account) : IInstrumentRulesSource
{
    public VenueId Venue => SteamVenue.VenueId;

    public async Task<InstrumentRulesDefinitionInternal> Get(InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        if (!SteamInstrument.TryParse(symbol.Value, out SteamInstrument? instrument))
        {
            throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidRequest, "The symbol is not a Steam item.");
        }

        SellerFeeSchedule fees = await Fees(instrument, cancellationToken);
        decimal tick = instrument.Currency.MinorUnit * fees.Increment;
        int precision = instrument.Currency.Precision;
        var rules = new InstrumentRules(
            quantityStep: 1m,
            tick,
            minQuantity: 1m,
            fees.BuyerPrice(tick, precision),
            basePrecision: 0,
            precision);

        return new InstrumentRulesDefinitionInternal(
            SteamVenue.VenueId.Value,
            "steam",
            "community-market",
            symbol.Value,
            instrument.MarketHashName,
            instrument.Currency.Code,
            BasePrecisionIncrement: 1m,
            instrument.Currency.MinorUnit,
            rules);
    }

    private async Task<SellerFeeSchedule> Fees(SteamInstrument instrument, CancellationToken cancellationToken)
    {
        SteamWallet wallet;

        try
        {
            wallet = await account.Wallet(cancellationToken);
        }
        catch (SteamMarketDataException exception)
        {
            throw new InstrumentRulesException(
                exception.Unavailable ? InstrumentRulesErrorCodes.ProviderUnavailable : InstrumentRulesErrorCodes.InvalidProviderData,
                exception.Message);
        }

        return wallet.Currency == instrument.Currency
            ? wallet.SellerFees
            : throw new InstrumentRulesException(
                InstrumentRulesErrorCodes.InvalidRequest,
                $"The market-data account's wallet is in {wallet.Currency.Code}, not {instrument.Currency.Code}.");
    }
}