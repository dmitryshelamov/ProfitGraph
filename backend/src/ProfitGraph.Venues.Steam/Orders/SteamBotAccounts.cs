using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.Steam.Trading;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Loads a Steam bot for one call: its owner and credential, its item, the seller fees read for its wallet and its own
///     settings (ADR 0022, C9). A bot of another venue, or one whose stored settings cannot be read, is not guessed.
/// </summary>
internal sealed class SteamBotAccounts(IServiceScopeFactory scopes) : ISteamBotAccounts
{
    public async Task<SteamBotAccount> Get(Guid botId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Bot bot = await scope.ServiceProvider.GetRequiredService<IBotRepository>().Get(new BotId(botId), cancellationToken) ??
            throw new SteamOrderUnresolvedException("The bot does not exist.");

        if (bot.Series.Venue != SteamVenue.VenueId || !SteamInstrument.TryParse(bot.Series.Symbol.Value, out SteamInstrument? instrument))
        {
            throw new SteamOrderUnresolvedException("The bot is not a Steam bot.");
        }

        SteamBotSettings settings = bot.VenueSettings.Version == 0 ? SteamBotSettings.Defaults : SteamBotSettingsSchema.Parse(bot.VenueSettings);

        return new SteamBotAccount(botId, bot.OwnerId.Value, bot.CredentialId.Value, instrument, bot.FeeRates.SellerFees, settings);
    }
}