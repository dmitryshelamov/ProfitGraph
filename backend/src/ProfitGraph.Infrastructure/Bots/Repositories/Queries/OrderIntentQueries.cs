namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class OrderIntentQueries
{
    private const string Columns = """
                                   id AS Id, bot_id AS BotId, engine_order_id AS EngineOrderId, kind AS Kind, client_order_id AS ClientOrderId,
                                   payload::text AS Payload, from_kill AS FromKill, status AS Status, attempts AS Attempts,
                                   next_attempt_at AS NextAttemptAt, claimed_at AS ClaimedAt, place_started_at AS PlaceStartedAt,
                                   sent_at AS SentAt, rejection_reason AS RejectionReason, rejection_message AS RejectionMessage,
                                   created_at AS CreatedAt, confirmed_absent_at AS ConfirmedAbsentAt, confirmed_absent_by AS ConfirmedAbsentBy,
                                   deferral_code AS DeferralCode, deferral_since AS DeferralSince, deferral_until AS DeferralUntil,
                                   deferral_low AS DeferralLow, deferral_high AS DeferralHigh
                                   """;

    internal const string GetForUpdate = $"SELECT {Columns} FROM bot_order_intents WHERE id = @id FOR UPDATE";

    internal const string Get = $"SELECT {Columns} FROM bot_order_intents WHERE id = @id";

    internal const string GetSubmit = $"SELECT {Columns} FROM bot_order_intents WHERE bot_id = @botId AND engine_order_id = @engineOrderId AND kind = 'Submit'";

    internal const string ListByBot = $"SELECT {Columns} FROM bot_order_intents WHERE bot_id = @botId ORDER BY sequence";

    internal const string Insert = """
                                   INSERT INTO bot_order_intents (
                                       id, bot_id, engine_order_id, kind, client_order_id, payload, from_kill, status, attempts,
                                       next_attempt_at, claimed_at, place_started_at, sent_at, rejection_reason, rejection_message, created_at,
                                       deferral_code, deferral_since, deferral_until, deferral_low, deferral_high)
                                   VALUES (
                                       @Id, @BotId, @EngineOrderId, @Kind, @ClientOrderId, CAST(@Payload AS jsonb), @FromKill, @Status, @Attempts,
                                       @NextAttemptAt, @ClaimedAt, @PlaceStartedAt, @SentAt, @RejectionReason, @RejectionMessage, @CreatedAt,
                                       @DeferralCode, @DeferralSince, @DeferralUntil, @DeferralLow, @DeferralHigh)
                                   ON CONFLICT (bot_id, engine_order_id, kind) DO NOTHING
                                   """;

    internal const string Update = """
                                   UPDATE bot_order_intents SET
                                       status = @Status, attempts = @Attempts, next_attempt_at = @NextAttemptAt, claimed_at = @ClaimedAt,
                                       place_started_at = @PlaceStartedAt, sent_at = @SentAt, rejection_reason = @RejectionReason,
                                       rejection_message = @RejectionMessage, confirmed_absent_at = @ConfirmedAbsentAt,
                                       confirmed_absent_by = @ConfirmedAbsentBy, deferral_code = @DeferralCode, deferral_since = @DeferralSince,
                                       deferral_until = @DeferralUntil, deferral_low = @DeferralLow, deferral_high = @DeferralHigh
                                   WHERE id = @Id
                                   """;

    internal const string ListDeliveryCandidates = """
                                                   SELECT intent.id FROM bot_order_intents AS intent
                                                   JOIN bots AS bot ON bot.id = intent.bot_id
                                                   LEFT JOIN bot_leases AS lease ON lease.bot_id = intent.bot_id
                                                   WHERE bot.venue = ANY(@venues)
                                                       AND (intent.status = 'Pending' AND (intent.next_attempt_at IS NULL OR intent.next_attempt_at <= @now)
                                                           OR intent.status = 'Deferred' AND intent.next_attempt_at <= @now
                                                           OR intent.status = 'Sending' AND intent.claimed_at <= @staleBefore)
                                                       AND (intent.kind = 'Submit' AND lease.holder_id = @holderId AND lease.expires_at > @now
                                                           OR intent.kind = 'Cancel' AND (lease.bot_id IS NULL OR lease.expires_at <= @now OR lease.holder_id = @holderId))
                                                       AND NOT EXISTS (
                                                           SELECT 1 FROM bot_order_intents AS earlier
                                                           WHERE earlier.bot_id = intent.bot_id AND earlier.sequence < intent.sequence
                                                               AND earlier.status IN ('Pending', 'Sending')
                                                               AND NOT (intent.kind = 'Cancel' AND earlier.kind = 'Submit'
                                                                   AND (bot.desired_state = 'Stopped' OR earlier.engine_order_id <> intent.engine_order_id)))
                                                   ORDER BY intent.sequence
                                                   LIMIT @limit
                                                   """;

    internal const string ListStoppedBotsWithWaitingSubmits = """
                                                              SELECT DISTINCT bot.id FROM bots AS bot
                                                              JOIN bot_order_intents AS intent ON intent.bot_id = bot.id
                                                              WHERE bot.desired_state = 'Stopped' AND intent.kind = 'Submit' AND intent.status IN ('Pending', 'Sending')
                                                              """;
}