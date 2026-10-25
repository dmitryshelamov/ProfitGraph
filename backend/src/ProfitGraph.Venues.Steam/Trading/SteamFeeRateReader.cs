using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam.MarketData;

namespace ProfitGraph.Venues.Steam.Trading;

/// <summary>
///     A Steam bot's fees are its wallet's seller fees (ADR 0022, C3), read with the bot's own credential. The wallet must be in
///     the item's currency; a bot never trades in a currency its wallet does not hold.
/// </summary>
internal sealed class SteamFeeRateReader(SteamMarketDataAccount account) : IVenueFeeRateReader
{
    public VenueId Venue => SteamVenue.VenueId;

    public async Task<VenueFeeRatesInternal> Read(ExchangeCredentialSecretsInternal credential, InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        if (!SteamInstrument.TryParse(symbol.Value, out SteamInstrument? instrument))
        {
            throw new VenueFeeRatesUnavailableException("The symbol is not a Steam item.");
        }

        try
        {
            SteamWallet wallet = await account.WalletOf(credential.CredentialId, cancellationToken);

            return wallet.Currency == instrument.Currency
                ? new VenueFeeRatesInternal(MakerRate: 0m, TakerRate: 0m, wallet.SellerFees)
                : throw new VenueFeeRatesUnavailableException($"The Steam wallet is in {wallet.Currency.Code}, not {instrument.Currency.Code}.");
        }
        catch (SteamMarketDataException exception)
        {
            throw new VenueFeeRatesUnavailableException(exception.Message);
        }
    }
}