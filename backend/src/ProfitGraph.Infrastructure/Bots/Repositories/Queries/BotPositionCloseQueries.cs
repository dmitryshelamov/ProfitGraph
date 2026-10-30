namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotPositionCloseQueries
{
    private const string Columns = """
                                   id AS Id, bot_id AS BotId, stop_after AS StopAfter, status AS Status, cancelled_orders AS CancelledOrders,
                                       engine_order_id AS EngineOrderId, failure_reason AS FailureReason, created_at AS CreatedAt, resolved_at AS ResolvedAt
                                   """;

    internal const string Get = $"SELECT {Columns} FROM bot_position_closes WHERE id = @id";

    internal const string GetForUpdate = $"SELECT {Columns} FROM bot_position_closes WHERE id = @id FOR UPDATE";

    internal const string GetActive = $"SELECT {Columns} FROM bot_position_closes WHERE bot_id = @botId AND status IN ('Pending', 'Cancelling', 'Selling')";

    internal const string GetLatest = $"SELECT {Columns} FROM bot_position_closes WHERE bot_id = @botId ORDER BY created_at DESC LIMIT 1";

    internal const string Insert = """
                                   INSERT INTO bot_position_closes (id, bot_id, stop_after, status, cancelled_orders, engine_order_id, failure_reason,
                                       created_at, resolved_at)
                                   VALUES (@Id, @BotId, @StopAfter, @Status, @CancelledOrders, @EngineOrderId, @FailureReason, @CreatedAt, @ResolvedAt)
                                   ON CONFLICT (bot_id) WHERE status IN ('Pending', 'Cancelling', 'Selling') DO NOTHING
                                   """;

    internal const string Update = """
                                   UPDATE bot_position_closes SET status = @Status, cancelled_orders = @CancelledOrders, engine_order_id = @EngineOrderId,
                                       failure_reason = @FailureReason, resolved_at = @ResolvedAt
                                   WHERE id = @Id
                                   """;
}