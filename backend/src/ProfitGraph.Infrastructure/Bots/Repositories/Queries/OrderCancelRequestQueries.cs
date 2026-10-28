namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class OrderCancelRequestQueries
{
    private const string Columns = """
                                   id AS Id, bot_id AS BotId, engine_order_id AS EngineOrderId, status AS Status, rejection_reason AS RejectionReason,
                                       created_at AS CreatedAt, resolved_at AS ResolvedAt
                                   """;

    internal const string Get = $"SELECT {Columns} FROM bot_order_cancels WHERE id = @id";

    internal const string GetForUpdate = $"SELECT {Columns} FROM bot_order_cancels WHERE id = @id FOR UPDATE";

    internal const string GetPending = $"SELECT {Columns} FROM bot_order_cancels WHERE bot_id = @botId AND status = 'Pending'";

    internal const string Insert = """
                                   INSERT INTO bot_order_cancels (id, bot_id, engine_order_id, status, rejection_reason, created_at, resolved_at)
                                   VALUES (@Id, @BotId, @EngineOrderId, @Status, @RejectionReason, @CreatedAt, @ResolvedAt)
                                   ON CONFLICT (bot_id) WHERE status = 'Pending' DO NOTHING
                                   """;

    internal const string Update = """
                                   UPDATE bot_order_cancels SET status = @Status, rejection_reason = @RejectionReason, resolved_at = @ResolvedAt
                                   WHERE id = @Id
                                   """;
}