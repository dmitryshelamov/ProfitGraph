using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Domain.Notifications;

public interface ITelegramLinkRepository
{
    Task<TelegramLink?> Get(UserId userId, CancellationToken cancellationToken);

    Task<TelegramLink?> GetByChatForUpdate(long chatId, CancellationToken cancellationToken);

    /// <summary>
    ///     Links the chat to the user, replacing the user's earlier chat and the chat's earlier user.
    /// </summary>
    Task Save(TelegramLink link, CancellationToken cancellationToken);

    Task Update(TelegramLink link, CancellationToken cancellationToken);

    Task<bool> Delete(UserId userId, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores a code, replacing the user's earlier one.
    /// </summary>
    Task SaveCode(TelegramLinkCode code, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes and returns the code with this hash, so it is used at most once.
    /// </summary>
    Task<TelegramLinkCode?> TakeCode(string codeHash, CancellationToken cancellationToken);

    /// <summary>
    ///     Alerts raised after their owner's chat was linked, not yet sent, for chats that are not blocked; oldest first.
    /// </summary>
    Task<IReadOnlyList<TelegramAlertDelivery>> ListUndelivered(int limit, CancellationToken cancellationToken);

    Task MarkDelivered(BotAlertId alertId, DateTimeOffset now, CancellationToken cancellationToken);
}