namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScanAutoTradeRuleQueries
{
    private const string Columns = """
                                   rule.id AS Id, rule.owner_id AS OwnerId, rule.preset_id AS PresetId, rule.venue AS Venue, rule.direction AS Direction,
                                   rule.scenario AS Scenario, rule.timeframe AS Timeframe, rule.min_expected_r AS MinExpectedR, rule.min_chance AS MinChance,
                                   rule.max_open_trades AS MaxOpenTrades, rule.max_trades_per_day AS MaxTradesPerDay, rule.daily_loss_limit AS DailyLossLimit,
                                   rule.mode AS Mode, rule.proof::text AS ProofJson, rule.paused_on AS PausedOn, rule.checked_at AS CheckedAt,
                                   rule.pairs::text AS Pairs, rule.mode_since AS ModeSince, rule.created_at AS CreatedAt, rule.updated_at AS UpdatedAt
                                   """;

    internal const string GetByPreset = $"SELECT {Columns} FROM scan_auto_trade_rules AS rule WHERE rule.preset_id = @PresetId AND rule.owner_id = @OwnerId";

    internal const string GetByPresetForUpdate =
        $"SELECT {Columns} FROM scan_auto_trade_rules AS rule WHERE rule.preset_id = @PresetId AND rule.owner_id = @OwnerId FOR UPDATE";

    internal const string GetForUpdate = $"SELECT {Columns} FROM scan_auto_trade_rules AS rule WHERE rule.id = @Id FOR UPDATE";

    internal const string ListOnForUpdate = $"""
                                             SELECT {Columns}
                                             FROM scan_auto_trade_rules AS rule
                                             WHERE rule.venue = @Venue AND rule.mode <> 'Off'
                                             ORDER BY rule.id
                                             FOR UPDATE
                                             """;

    internal const string Add = """
                                INSERT INTO scan_auto_trade_rules (id, owner_id, preset_id, venue, direction, scenario, timeframe, min_expected_r, min_chance,
                                    max_open_trades, max_trades_per_day, daily_loss_limit, mode, proof, paused_on, checked_at, pairs, mode_since, created_at, updated_at)
                                VALUES (@Id, @OwnerId, @PresetId, @Venue, @Direction, @Scenario, @Timeframe, @MinExpectedR, @MinChance, @MaxOpenTrades,
                                    @MaxTradesPerDay, @DailyLossLimit, @Mode, CAST(@ProofJson AS jsonb), @PausedOn, @CheckedAt, CAST(@Pairs AS jsonb), @ModeSince, @CreatedAt,
                                    @UpdatedAt)
                                """;

    internal const string Update = """
                                   UPDATE scan_auto_trade_rules
                                   SET direction = @Direction, scenario = @Scenario, timeframe = @Timeframe, min_expected_r = @MinExpectedR,
                                       min_chance = @MinChance, max_open_trades = @MaxOpenTrades, max_trades_per_day = @MaxTradesPerDay,
                                       daily_loss_limit = @DailyLossLimit, mode = @Mode, proof = CAST(@ProofJson AS jsonb), paused_on = @PausedOn,
                                       checked_at = @CheckedAt, pairs = CAST(@Pairs AS jsonb), mode_since = @ModeSince, updated_at = @UpdatedAt
                                   WHERE id = @Id
                                   """;

    internal const string Delete = "DELETE FROM scan_auto_trade_rules WHERE id = @Id";
}