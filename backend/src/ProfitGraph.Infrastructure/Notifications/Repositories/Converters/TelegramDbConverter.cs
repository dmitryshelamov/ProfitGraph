using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Infrastructure.Notifications.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Notifications.Repositories.Converters;

internal static class TelegramDbConverter
{
    public static TelegramLink ToDomain(this TelegramLinkDb row)
    {
        return TelegramLink.Restore(new UserId(row.UserId), row.ChatId, row.LinkedAt, row.BlockedAt);
    }

    public static TelegramLinkDb ToDb(this TelegramLink link)
    {
        return new TelegramLinkDb { UserId = link.UserId.Value, ChatId = link.ChatId, LinkedAt = link.LinkedAt, BlockedAt = link.BlockedAt };
    }

    public static TelegramLinkCode ToDomain(this TelegramLinkCodeDb row)
    {
        return new TelegramLinkCode(row.CodeHash, new UserId(row.UserId), row.ExpiresAt);
    }

    public static TelegramLinkCodeDb ToDb(this TelegramLinkCode code)
    {
        return new TelegramLinkCodeDb { CodeHash = code.CodeHash, UserId = code.UserId.Value, ExpiresAt = code.ExpiresAt };
    }
}