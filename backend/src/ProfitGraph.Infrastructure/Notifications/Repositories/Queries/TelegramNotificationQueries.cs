namespace ProfitGraph.Infrastructure.Notifications.Repositories.Queries;

internal static class TelegramNotificationQueries
{
    private const string Columns = """
                                   id AS Id, owner_id AS OwnerId, bot_id AS BotId, kind AS Kind, alert_id AS AlertId, details::text AS Details,
                                   created_at AS CreatedAt, status AS Status, attempts AS Attempts, next_attempt_at AS NextAttemptAt, claim_id AS ClaimId,
                                   claimed_until AS ClaimedUntil, sent_at AS SentAt, last_error AS LastError
                                   """;

    internal const string Insert = """
                                   INSERT INTO telegram_notifications (id, owner_id, bot_id, kind, alert_id, details, created_at, status, attempts, next_attempt_at,
                                       claim_id, claimed_until, sent_at, last_error)
                                   VALUES (@Id, @OwnerId, @BotId, @Kind, @AlertId, CAST(@Details AS jsonb), @CreatedAt, @Status, @Attempts, @NextAttemptAt,
                                       @ClaimId, @ClaimedUntil, @SentAt, @LastError)
                                   """;

    internal const string LockDue = $"""
                                     SELECT {Columns}
                                     FROM telegram_notifications
                                     WHERE status = 'Pending' AND next_attempt_at <= @now AND (claimed_until IS NULL OR claimed_until <= @now)
                                     ORDER BY created_at, id
                                     LIMIT @limit
                                     FOR UPDATE SKIP LOCKED
                                     """;

    internal const string GetForUpdate = $"SELECT {Columns} FROM telegram_notifications WHERE id = @id FOR UPDATE";

    internal const string Update = """
                                   UPDATE telegram_notifications
                                   SET status = @Status, attempts = @Attempts, next_attempt_at = @NextAttemptAt, claim_id = @ClaimId, claimed_until = @ClaimedUntil,
                                       sent_at = @SentAt, last_error = @LastError
                                   WHERE id = @Id
                                   """;

    internal const string DeleteFinishedBefore = "DELETE FROM telegram_notifications WHERE status <> 'Pending' AND created_at < @cutoff";
}