namespace ProfitGraph.Infrastructure.Common.Outbox;

internal static class OutboxQueries
{
    internal const string Insert = """
                                   INSERT INTO outbox_messages (id, type, payload, created_at)
                                   VALUES (@Id, @Type, CAST(@Payload AS jsonb), @CreatedAt)
                                   """;

    internal const string ClaimNext = """
                                      SELECT id AS Id, type AS Type, payload::text AS Payload, attempts AS Attempts, created_at AS CreatedAt
                                      FROM outbox_messages
                                      WHERE delivered_at IS NULL AND (next_attempt_at IS NULL OR next_attempt_at <= @now)
                                      ORDER BY sequence
                                      FOR UPDATE SKIP LOCKED
                                      LIMIT 1
                                      """;

    internal const string MarkDelivered = "UPDATE outbox_messages SET delivered_at = @now, last_error = NULL WHERE id = @id";

    internal const string RecordFailure = """
                                          UPDATE outbox_messages SET attempts = attempts + 1, next_attempt_at = @nextAttemptAt, last_error = @error
                                          WHERE id = @id AND delivered_at IS NULL
                                          """;
}