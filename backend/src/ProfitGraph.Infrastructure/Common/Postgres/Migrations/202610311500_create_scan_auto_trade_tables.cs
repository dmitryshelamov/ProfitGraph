using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Auto trades of the scanner (ADR 0071, sections 4–7): each backtest's closed card trades and the move of the venue's reference pair
///     over its period, one rule per preset with its remembered pairs, the requests an entering pair stores and the paper trades.
/// </summary>
[Migration(202610311500)]
public sealed class CreateScanAutoTradeTablesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE scan_backtests ADD COLUMN market_change numeric NULL;

            CREATE TABLE scan_backtest_cards (
                backtest_id uuid NOT NULL REFERENCES scan_backtests (id) ON DELETE CASCADE,
                timeframe varchar(8) NOT NULL,
                scenario varchar(32) NOT NULL,
                direction varchar(8) NOT NULL,
                signal_at timestamp with time zone NOT NULL,
                third smallint NOT NULL CHECK (third BETWEEN 0 AND 2),
                result_r numeric NOT NULL,
                chance numeric NULL,
                expected_r numeric NULL
            );

            CREATE INDEX scan_backtest_cards_kind ON scan_backtest_cards (backtest_id, timeframe, scenario, direction);

            CREATE TABLE scan_auto_trade_rules (
                id uuid PRIMARY KEY,
                owner_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                preset_id uuid NOT NULL UNIQUE REFERENCES scan_presets (id) ON DELETE CASCADE,
                venue varchar(64) NOT NULL,
                direction varchar(8) NOT NULL,
                scenario varchar(32) NOT NULL,
                timeframe varchar(8) NOT NULL,
                min_expected_r numeric NOT NULL,
                min_chance numeric NULL,
                max_open_trades integer NOT NULL,
                max_trades_per_day integer NOT NULL,
                daily_loss_limit numeric NOT NULL,
                mode varchar(8) NOT NULL,
                proof jsonb NULL,
                paused_on date NULL,
                checked_at timestamp with time zone NULL,
                pairs jsonb NOT NULL,
                mode_since timestamp with time zone NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );

            CREATE INDEX scan_auto_trade_rules_venue ON scan_auto_trade_rules (venue) WHERE mode <> 'Off';

            CREATE TABLE scan_auto_trade_requests (
                id uuid PRIMARY KEY,
                rule_id uuid NOT NULL REFERENCES scan_auto_trade_rules (id) ON DELETE CASCADE,
                symbol varchar(256) NOT NULL,
                snapshot_at timestamp with time zone NOT NULL,
                price numeric NULL,
                created_at timestamp with time zone NOT NULL,
                status varchar(16) NOT NULL,
                error_code varchar(100) NULL,
                trade_id uuid NULL,
                handled_at timestamp with time zone NULL
            );

            CREATE INDEX scan_auto_trade_requests_pending ON scan_auto_trade_requests (created_at) WHERE status IN ('Pending', 'Handling');
            CREATE INDEX scan_auto_trade_requests_rule ON scan_auto_trade_requests (rule_id, created_at DESC);

            CREATE TABLE scan_paper_trades (
                id uuid PRIMARY KEY,
                rule_id uuid NOT NULL REFERENCES scan_auto_trade_rules (id) ON DELETE CASCADE,
                venue varchar(64) NOT NULL,
                symbol varchar(256) NOT NULL,
                timeframe varchar(8) NOT NULL,
                scenario varchar(32) NOT NULL,
                direction varchar(8) NOT NULL,
                entry numeric(38, 18) NOT NULL,
                stop numeric(38, 18) NOT NULL,
                target numeric(38, 18) NOT NULL,
                reward_risk numeric NOT NULL,
                entry_wait_minutes integer NOT NULL,
                amount numeric NOT NULL,
                risk numeric NOT NULL,
                limit_fee_rate numeric NOT NULL,
                market_fee_rate numeric NOT NULL,
                spread numeric NOT NULL,
                expected_r numeric NOT NULL,
                created_at timestamp with time zone NOT NULL,
                last_minute timestamp with time zone NOT NULL,
                waited integer NOT NULL,
                entered_at timestamp with time zone NULL,
                held integer NOT NULL,
                exit_reason varchar(8) NULL,
                filled boolean NULL,
                result_r numeric NULL,
                exited_at timestamp with time zone NULL
            );

            CREATE INDEX scan_paper_trades_open ON scan_paper_trades (venue) WHERE filled IS NULL;
            CREATE INDEX scan_paper_trades_rule ON scan_paper_trades (rule_id, created_at DESC);
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP TABLE scan_paper_trades;
            DROP TABLE scan_auto_trade_requests;
            DROP TABLE scan_auto_trade_rules;
            DROP TABLE scan_backtest_cards;
            ALTER TABLE scan_backtests DROP COLUMN market_change;
            """);
    }
}
