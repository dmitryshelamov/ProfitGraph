namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotOrderWaitingQueries
{
    private const string Columns = """
                                   bot_id AS BotId, engine_order_id AS EngineOrderId, code AS Code, since AS Since, until AS Until, low AS Low,
                                   high AS High
                                   """;

    internal const string List = $"SELECT {Columns} FROM bot_order_waiting WHERE bot_id = @botId ORDER BY since, engine_order_id";

    internal const string GetForUpdate = $"SELECT {Columns} FROM bot_order_waiting WHERE bot_id = @botId AND engine_order_id = @engineOrderId FOR UPDATE";

    internal const string Upsert = """
                                   INSERT INTO bot_order_waiting (bot_id, engine_order_id, code, since, until, low, high)
                                   VALUES (@BotId, @EngineOrderId, @Code, @Since, @Until, @Low, @High)
                                   ON CONFLICT (bot_id, engine_order_id) DO UPDATE SET
                                       code = EXCLUDED.code,
                                       since = EXCLUDED.since,
                                       until = EXCLUDED.until,
                                       low = EXCLUDED.low,
                                       high = EXCLUDED.high
                                   """;

    internal const string Delete = "DELETE FROM bot_order_waiting WHERE bot_id = @botId AND engine_order_id = @engineOrderId";
}