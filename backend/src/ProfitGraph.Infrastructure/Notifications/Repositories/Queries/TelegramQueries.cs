namespace ProfitGraph.Infrastructure.Notifications.Repositories.Queries;

internal static class TelegramQueries
{
    private const string LinkColumns = "user_id AS UserId, chat_id AS ChatId, linked_at AS LinkedAt, blocked_at AS BlockedAt";

    internal const string GetLink = $"SELECT {LinkColumns} FROM telegram_links WHERE user_id = @userId";

    internal const string GetLinkByChatForUpdate = $"SELECT {LinkColumns} FROM telegram_links WHERE chat_id = @chatId FOR UPDATE";

    internal const string SaveLink = """
                                     DELETE FROM telegram_links WHERE chat_id = @ChatId AND user_id <> @UserId;
                                     INSERT INTO telegram_links (user_id, chat_id, linked_at, blocked_at) VALUES (@UserId, @ChatId, @LinkedAt, @BlockedAt)
                                     ON CONFLICT (user_id) DO UPDATE SET chat_id = EXCLUDED.chat_id, linked_at = EXCLUDED.linked_at, blocked_at = EXCLUDED.blocked_at;
                                     """;

    internal const string UpdateLink = "UPDATE telegram_links SET blocked_at = @BlockedAt WHERE user_id = @UserId";

    internal const string DeleteLink = "DELETE FROM telegram_links WHERE user_id = @userId";

    internal const string SaveCode = """
                                     INSERT INTO telegram_link_codes (code_hash, user_id, expires_at) VALUES (@CodeHash, @UserId, @ExpiresAt)
                                     ON CONFLICT (user_id) DO UPDATE SET code_hash = EXCLUDED.code_hash, expires_at = EXCLUDED.expires_at
                                     """;

    internal const string TakeCode =
        "DELETE FROM telegram_link_codes WHERE code_hash = @codeHash RETURNING code_hash AS CodeHash, user_id AS UserId, expires_at AS ExpiresAt";

    internal const string ListUndelivered = """
                                            SELECT alert.id AS AlertId, link.chat_id AS ChatId, alert.kind AS Kind, alert.reason AS Reason, bot.symbol AS Symbol,
                                                bot.timeframe AS Timeframe
                                            FROM bot_alerts AS alert
                                            JOIN telegram_links AS link ON link.user_id = alert.owner_id
                                            JOIN bots AS bot ON bot.id = alert.bot_id
                                            WHERE alert.telegram_sent_at IS NULL AND link.blocked_at IS NULL AND alert.created_at >= link.linked_at
                                            ORDER BY alert.created_at, alert.id
                                            LIMIT @limit
                                            """;

    internal const string MarkDelivered = "UPDATE bot_alerts SET telegram_sent_at = @now WHERE id = @alertId AND telegram_sent_at IS NULL";

    internal const string AcquireLease = """
                                         INSERT INTO telegram_poller_lease (id, holder_id, expires_at) VALUES (true, @holderId, @expiresAt)
                                         ON CONFLICT (id) DO UPDATE SET holder_id = EXCLUDED.holder_id, expires_at = EXCLUDED.expires_at
                                         WHERE telegram_poller_lease.holder_id = EXCLUDED.holder_id OR telegram_poller_lease.expires_at <= @now
                                         RETURNING update_offset
                                         """;

    internal const string AdvanceOffset = """
                                          UPDATE telegram_poller_lease SET update_offset = @updateOffset
                                          WHERE holder_id = @holderId AND expires_at > @now AND update_offset < @updateOffset
                                          """;
}