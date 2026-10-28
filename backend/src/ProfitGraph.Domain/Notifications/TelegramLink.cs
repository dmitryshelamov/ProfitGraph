using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     A user's Telegram chat for notifications (ADR 0020, ADR 0042). It receives what happens after it was linked, until the user
///     blocks the bot or unlinks it.
/// </summary>
public sealed class TelegramLink : AggregateRoot
{
    public UserId UserId { get; }
    public long ChatId { get; }
    public DateTimeOffset LinkedAt { get; }
    public DateTimeOffset? BlockedAt { get; private set; }

    private TelegramLink(UserId userId, long chatId, DateTimeOffset linkedAt, DateTimeOffset? blockedAt)
    {
        UserId = userId;
        ChatId = chatId;
        LinkedAt = linkedAt;
        BlockedAt = blockedAt;
    }

    public static TelegramLink Link(UserId userId, long chatId, DateTimeOffset now)
    {
        return new TelegramLink(userId, chatId, now, blockedAt: null);
    }

    public static TelegramLink Restore(UserId userId, long chatId, DateTimeOffset linkedAt, DateTimeOffset? blockedAt)
    {
        return new TelegramLink(userId, chatId, linkedAt, blockedAt);
    }

    /// <summary>
    ///     Telegram refused a message because the user blocked the bot; nothing more is sent. Returns whether it changed.
    /// </summary>
    public bool Block(DateTimeOffset now)
    {
        if (BlockedAt is not null)
        {
            return false;
        }

        BlockedAt = now;

        return true;
    }
}