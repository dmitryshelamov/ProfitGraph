namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScanPaperTradeQueries
{
    private const string Columns = """
                                   id AS Id, rule_id AS RuleId, venue AS Venue, symbol AS Symbol, timeframe AS Timeframe, scenario AS Scenario,
                                   direction AS Direction, entry AS Entry, stop AS Stop, target AS Target, reward_risk AS RewardRisk,
                                   entry_wait_minutes AS EntryWaitMinutes, amount AS Amount, risk AS Risk, limit_fee_rate AS LimitFeeRate,
                                   market_fee_rate AS MarketFeeRate, spread AS Spread, expected_r AS ExpectedR, created_at AS CreatedAt,
                                   last_minute AS LastMinute, waited AS Waited, entered_at AS EnteredAt, held AS Held, exit_reason AS ExitReason,
                                   filled AS Filled, result_r AS ResultR, exited_at AS ExitedAt
                                   """;

    private const string Open = "(filled IS NULL)";

    internal const string Add = """
                                INSERT INTO scan_paper_trades (id, rule_id, venue, symbol, timeframe, scenario, direction, entry, stop, target, reward_risk,
                                    entry_wait_minutes, amount, risk, limit_fee_rate, market_fee_rate, spread, expected_r, created_at, last_minute, waited,
                                    entered_at, held, exit_reason, filled, result_r, exited_at)
                                VALUES (@Id, @RuleId, @Venue, @Symbol, @Timeframe, @Scenario, @Direction, @Entry, @Stop, @Target, @RewardRisk,
                                    @EntryWaitMinutes, @Amount, @Risk, @LimitFeeRate, @MarketFeeRate, @Spread, @ExpectedR, @CreatedAt, @LastMinute, @Waited,
                                    @EnteredAt, @Held, @ExitReason, @Filled, @ResultR, @ExitedAt)
                                """;

    internal const string ListOpenForUpdate = $"""
                                               SELECT {Columns}
                                               FROM scan_paper_trades
                                               WHERE venue = @Venue AND {Open}
                                               ORDER BY created_at, id
                                               FOR UPDATE SKIP LOCKED
                                               """;

    internal const string ListRecent = $"""
                                        SELECT {Columns}
                                        FROM scan_paper_trades
                                        WHERE rule_id = @RuleId AND (created_at >= @Since OR {Open} OR exited_at >= @Since)
                                        ORDER BY created_at DESC, id
                                        """;

    internal const string Update = """
                                   UPDATE scan_paper_trades
                                   SET last_minute = @LastMinute, waited = @Waited, entered_at = @EnteredAt, held = @Held, exit_reason = @ExitReason,
                                       filled = @Filled, result_r = @ResultR, exited_at = @ExitedAt
                                   WHERE id = @Id
                                   """;
}