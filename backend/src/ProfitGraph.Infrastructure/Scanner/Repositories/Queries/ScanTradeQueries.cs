namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScanTradeQueries
{
    private const string Columns = """
                                   id AS Id, owner_id AS OwnerId, venue AS Venue, symbol AS Symbol, scenario AS Scenario, levels_timeframe AS LevelsTimeframe, entry AS Entry, stop AS Stop,
                                   target AS Target, amount AS Amount, risk AS Risk, bot_id AS BotId, created_at AS CreatedAt, expires_at AS ExpiresAt,
                                   status AS Status, opened_at AS OpenedAt, closed_at AS ClosedAt, exit AS Exit, realized_pnl AS RealizedPnl,
                                   failure_code AS FailureCode, bot_released AS BotReleased
                                   """;

    private const string Live = "status IN ('Waiting', 'Open')";

    internal const string Add = """
                                INSERT INTO scan_trades (
                                    id, owner_id, venue, symbol, scenario, levels_timeframe, entry, stop, target, amount, risk, bot_id, created_at, expires_at, status,
                                    opened_at, closed_at, exit, realized_pnl, failure_code, bot_released)
                                VALUES (
                                    @Id, @OwnerId, @Venue, @Symbol, @Scenario, @LevelsTimeframe, @Entry, @Stop, @Target, @Amount, @Risk, @BotId, @CreatedAt, @ExpiresAt, @Status,
                                    @OpenedAt, @ClosedAt, @Exit, @RealizedPnl, @FailureCode, @BotReleased)
                                """;

    internal const string Update = """
                                   UPDATE scan_trades
                                   SET status = @Status, opened_at = @OpenedAt, closed_at = @ClosedAt, exit = @Exit, realized_pnl = @RealizedPnl,
                                       failure_code = @FailureCode, bot_released = @BotReleased
                                   WHERE id = @Id
                                   """;

    internal const string Get = $"SELECT {Columns} FROM scan_trades WHERE id = @Id";

    internal const string GetForUpdate = $"SELECT {Columns} FROM scan_trades WHERE id = @Id FOR UPDATE";

    internal const string GetByBotForUpdate = $"SELECT {Columns} FROM scan_trades WHERE bot_id = @BotId FOR UPDATE";

    internal const string ListLive = $"SELECT {Columns} FROM scan_trades WHERE {Live} ORDER BY created_at, id LIMIT @Limit";

    internal const string ListUnreleased = $"SELECT {Columns} FROM scan_trades WHERE NOT ({Live}) AND NOT bot_released ORDER BY created_at, id LIMIT @Limit";

    internal const string List = $"""
                                  SELECT {Columns}
                                  FROM scan_trades
                                  WHERE owner_id = @OwnerId AND venue = @Venue AND (CAST(@Symbol AS varchar) IS NULL OR symbol = @Symbol)
                                  ORDER BY created_at DESC, id DESC
                                  LIMIT @Limit
                                  """;
}