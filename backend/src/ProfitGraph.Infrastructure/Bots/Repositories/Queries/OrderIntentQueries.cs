namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class OrderIntentQueries
{
    private const string Columns = """
                                   id AS Id, bot_id AS BotId, engine_order_id AS EngineOrderId, kind AS Kind, client_order_id AS ClientOrderId,
                                   payload::text AS Payload, from_kill AS FromKill, status AS Status, attempts AS Attempts,
                                   next_attempt_at AS NextAttemptAt, claimed_at AS ClaimedAt, place_started_at AS PlaceStartedAt,
                                   sent_at AS SentAt, rejection_reason AS RejectionReason, rejection_message AS RejectionMessage,
                                   created_at AS CreatedAt
                                   """;

    internal const string GetForUpdate = $"SELECT {Columns} FROM bot_order_intents WHERE id = @id FOR UPDATE";

    internal const string ListByBot = $"SELECT {Columns} FROM bot_order_intents WHERE bot_id = @botId ORDER BY sequence";

    internal const string Insert = """
                                   INSERT INTO bot_order_intents (
                                       id, bot_id, engine_order_id, kind, client_order_id, payload, from_kill, status, attempts,
                                       next_attempt_at, claimed_at, place_started_at, sent_at, rejection_reason, rejection_message, created_at)
                                   VALUES (
                                       @Id, @BotId, @EngineOrderId, @Kind, @ClientOrderId, CAST(@Payload AS jsonb), @FromKill, @Status, @Attempts,
                                       @NextAttemptAt, @ClaimedAt, @PlaceStartedAt, @SentAt, @RejectionReason, @RejectionMessage, @CreatedAt)
                                   ON CONFLICT (bot_id, engine_order_id, kind) DO NOTHING
                                   """;

    internal const string Update = """
                                   UPDATE bot_order_intents SET
                                       status = @Status, attempts = @Attempts, next_attempt_at = @NextAttemptAt, claimed_at = @ClaimedAt,
                                       place_started_at = @PlaceStartedAt, sent_at = @SentAt, rejection_reason = @RejectionReason,
                                       rejection_message = @RejectionMessage
                                   WHERE id = @Id
                                   """;
}