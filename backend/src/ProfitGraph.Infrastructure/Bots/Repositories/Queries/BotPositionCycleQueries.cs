namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotPositionCycleQueries
{
    private const string Columns = """
                                   id AS Id, bot_id AS BotId, opened_at AS OpenedAt, realized_pnl_at_open AS RealizedPnlAtOpen, closed_at AS ClosedAt,
                                       realized_pnl AS RealizedPnl
                                   """;

    internal const string GetOpenForUpdate = $"SELECT {Columns} FROM bot_position_cycles WHERE bot_id = @botId AND closed_at IS NULL FOR UPDATE";

    internal const string List = $"SELECT {Columns} FROM bot_position_cycles WHERE bot_id = @botId ORDER BY opened_at DESC, id LIMIT @limit";

    internal const string Insert = """
                                   INSERT INTO bot_position_cycles (id, bot_id, opened_at, realized_pnl_at_open, closed_at, realized_pnl)
                                   VALUES (@Id, @BotId, @OpenedAt, @RealizedPnlAtOpen, @ClosedAt, @RealizedPnl)
                                   """;

    internal const string Update = "UPDATE bot_position_cycles SET closed_at = @ClosedAt, realized_pnl = @RealizedPnl WHERE id = @Id";
}