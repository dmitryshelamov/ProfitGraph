using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     Loads a bot for its owner. Another user's bot is reported as missing, so its existence is not revealed.
/// </summary>
internal static class OwnedBots
{
    internal static async Task<Bot> GetOwned(this IBotRepository bots, Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        Bot? bot = id == Guid.Empty ? null : await bots.Get(new BotId(id), cancellationToken);

        return Owned(bot, ownerId);
    }

    /// <summary>
    ///     Loads and locks the owner's bot inside the current transaction.
    /// </summary>
    internal static async Task<Bot> GetOwnedForUpdate(this IBotRepository bots, Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        Bot? bot = id == Guid.Empty ? null : await bots.GetForUpdate(new BotId(id), cancellationToken);

        return Owned(bot, ownerId);
    }

    private static Bot Owned(Bot? bot, Guid ownerId)
    {
        return bot is not null && bot.IsOwnedBy(new UserId(ownerId)) ? bot : throw new ResourceNotFoundException("The bot does not exist.", BotErrorCodes.NotFound);
    }
}