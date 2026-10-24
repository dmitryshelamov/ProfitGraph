using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610240009)]
public sealed class CreateBacktestRunTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE backtest_runs (
                id uuid PRIMARY KEY REFERENCES background_jobs (id),
                owner_id uuid NOT NULL REFERENCES users (id),
                strategy_id uuid NOT NULL,
                strategy_version integer NOT NULL,
                created_at timestamp with time zone NOT NULL,
                configuration jsonb NOT NULL,
                stage varchar(16) NULL CHECK (stage IN ('loading_history', 'replaying')),
                history_identity varchar(64) NULL,
                history_from timestamp with time zone NULL,
                replay_from timestamp with time zone NULL,
                replay_to timestamp with time zone NULL,
                warmup_count integer NULL CHECK (warmup_count >= 0),
                replay_count integer NULL CHECK (replay_count > 0),
                rules_snapshot_id uuid NULL REFERENCES instrument_rules (id),
                base_asset varchar(32) NULL,
                quote_asset varchar(32) NULL,
                outcome varchar(16) NULL CHECK (outcome IN ('completed', 'cancelled', 'failed')),
                processed_candles integer NULL CHECK (processed_candles >= 0),
                equity numeric NULL,
                total_pnl numeric NULL,
                failure_code varchar(64) NULL,
                failure_message text NULL,
                metrics jsonb NULL,
                ledger_count bigint NOT NULL DEFAULT 0 CHECK (ledger_count >= 0),
                candle_count bigint NOT NULL DEFAULT 0 CHECK (candle_count >= 0),
                diagnostic_count bigint NOT NULL DEFAULT 0 CHECK (diagnostic_count >= 0),
                FOREIGN KEY (strategy_id, strategy_version) REFERENCES strategy_versions (strategy_id, version_number),
                CHECK ((stage = 'replaying') = (history_identity IS NOT NULL)),
                CHECK (outcome IS NULL OR stage = 'replaying')
            );

            CREATE INDEX backtest_runs_strategy_created ON backtest_runs (strategy_id, created_at DESC, id);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE backtest_runs;");
    }
}
