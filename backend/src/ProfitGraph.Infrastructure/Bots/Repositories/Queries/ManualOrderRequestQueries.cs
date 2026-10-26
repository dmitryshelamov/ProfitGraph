namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class ManualOrderRequestQueries
{
    private const string Columns = """
                                   id AS Id, bot_id AS BotId, side AS Side, order_type AS OrderType, amount AS Amount, limit_price AS LimitPrice,
                                       status AS Status, engine_order_id AS EngineOrderId, rejection_reason AS RejectionReason, created_at AS CreatedAt,
                                       resolved_at AS ResolvedAt
                                   """;

    internal const string Get = $"SELECT {Columns} FROM bot_manual_orders WHERE id = @id";

    internal const string GetForUpdate = $"SELECT {Columns} FROM bot_manual_orders WHERE id = @id FOR UPDATE";

    internal const string GetPending = $"SELECT {Columns} FROM bot_manual_orders WHERE bot_id = @botId AND status = 'Pending'";

    internal const string Insert = """
                                   INSERT INTO bot_manual_orders (id, bot_id, side, order_type, amount, limit_price, status, engine_order_id,
                                       rejection_reason, created_at, resolved_at)
                                   VALUES (@Id, @BotId, @Side, @OrderType, @Amount, @LimitPrice, @Status, @EngineOrderId, @RejectionReason, @CreatedAt,
                                       @ResolvedAt)
                                   ON CONFLICT (bot_id) WHERE status = 'Pending' DO NOTHING
                                   """;

    internal const string Update = """
                                   UPDATE bot_manual_orders SET status = @Status, engine_order_id = @EngineOrderId, rejection_reason = @RejectionReason,
                                       resolved_at = @ResolvedAt
                                   WHERE id = @Id
                                   """;
}