namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     A Steam bot's account, item, wallet fees and own settings, as stored with the bot.
/// </summary>
internal interface ISteamBotAccounts
{
    Task<SteamBotAccount> Get(Guid botId, CancellationToken cancellationToken);
}