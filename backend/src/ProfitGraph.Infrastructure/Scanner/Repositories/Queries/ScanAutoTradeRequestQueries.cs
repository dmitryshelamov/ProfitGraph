namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScanAutoTradeRequestQueries
{
    private const string Columns = """
                                   id AS Id, rule_id AS RuleId, symbol AS Symbol, snapshot_at AS SnapshotAt, price AS Price, created_at AS CreatedAt,
                                   status AS Status, error_code AS ErrorCode, trade_id AS TradeId, handled_at AS HandledAt
                                   """;

    internal const string Add = """
                                INSERT INTO scan_auto_trade_requests (id, rule_id, symbol, snapshot_at, price, created_at, status, error_code, trade_id, handled_at)
                                VALUES (@Id, @RuleId, @Symbol, @SnapshotAt, @Price, @CreatedAt, @Status, @ErrorCode, @TradeId, @HandledAt)
                                """;

    internal const string ClaimPending = $"""
                                          SELECT {Columns}
                                          FROM scan_auto_trade_requests
                                          WHERE status = 'Pending' OR (status = 'Handling' AND snapshot_at < @StaleBefore)
                                          ORDER BY created_at, id
                                          LIMIT @Limit
                                          FOR UPDATE SKIP LOCKED
                                          """;

    internal const string GetForUpdate = $"SELECT {Columns} FROM scan_auto_trade_requests WHERE id = @Id FOR UPDATE";

    internal const string Update = """
                                   UPDATE scan_auto_trade_requests SET status = @Status, error_code = @ErrorCode, trade_id = @TradeId, handled_at = @HandledAt
                                   WHERE id = @Id
                                   """;

    internal const string DeleteBefore = "DELETE FROM scan_auto_trade_requests WHERE created_at < @Cutoff";

    internal const string ListByRule = $"""
                                        SELECT {Columns}
                                        FROM scan_auto_trade_requests
                                        WHERE rule_id = @RuleId
                                        ORDER BY created_at DESC, id
                                        LIMIT @Limit
                                        """;
}