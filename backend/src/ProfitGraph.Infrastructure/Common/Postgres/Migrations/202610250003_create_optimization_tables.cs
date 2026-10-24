using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250003)]
public sealed class CreateOptimizationTablesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE optimizations (
                id uuid PRIMARY KEY REFERENCES background_jobs (id),
                owner_id uuid NOT NULL REFERENCES users (id),
                strategy_id uuid NOT NULL,
                strategy_version integer NOT NULL,
                created_at timestamp with time zone NOT NULL,
                configuration jsonb NOT NULL,
                stage varchar(24) NULL CHECK (stage IN ('loading_history', 'searching', 'starting_finalists')),
                history_identity varchar(64) NULL,
                history_from timestamp with time zone NULL,
                replay_from timestamp with time zone NULL,
                replay_to timestamp with time zone NULL,
                warmup_count integer NULL CHECK (warmup_count >= 0),
                replay_count integer NULL CHECK (replay_count > 0),
                rules_snapshot_id uuid NULL REFERENCES instrument_rules (id),
                base_asset varchar(32) NULL,
                quote_asset varchar(32) NULL,
                planned_trials integer NULL CHECK (planned_trials >= 0),
                completed integer NOT NULL DEFAULT 0 CHECK (completed >= 0),
                rejected_by_constraint integer NOT NULL DEFAULT 0 CHECK (rejected_by_constraint >= 0),
                rejected_by_template integer NOT NULL DEFAULT 0 CHECK (rejected_by_template >= 0),
                failed integer NOT NULL DEFAULT 0 CHECK (failed >= 0),
                best_trial_index integer NULL,
                best_objective numeric NULL,
                FOREIGN KEY (strategy_id, strategy_version) REFERENCES strategy_versions (strategy_id, version_number),
                CHECK ((stage IN ('searching', 'starting_finalists')) = (history_identity IS NOT NULL)),
                CHECK ((best_trial_index IS NULL) = (best_objective IS NULL))
            );

            CREATE INDEX optimizations_strategy_created ON optimizations (strategy_id, created_at DESC, id);

            CREATE TABLE optimization_trials (
                optimization_id uuid NOT NULL REFERENCES optimizations (id),
                sequence bigint NOT NULL CHECK (sequence > 0),
                trial_index integer NOT NULL CHECK (trial_index >= 0),
                parameters jsonb NOT NULL,
                status varchar(24) NOT NULL CHECK (status IN ('completed', 'rejected_by_constraint', 'rejected_by_template', 'failed')),
                failure_code varchar(64) NULL,
                objective numeric NULL,
                return_percent numeric NULL,
                total_pnl numeric NULL,
                max_drawdown_percent numeric NULL,
                executions bigint NOT NULL CHECK (executions >= 0),
                elapsed_ms bigint NOT NULL CHECK (elapsed_ms >= 0),
                PRIMARY KEY (optimization_id, sequence),
                UNIQUE (optimization_id, trial_index)
            );

            ALTER TABLE backtest_runs ADD COLUMN optimization_id uuid NULL REFERENCES optimizations (id);

            CREATE INDEX backtest_runs_optimization ON backtest_runs (optimization_id, created_at, id) WHERE optimization_id IS NOT NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP INDEX backtest_runs_optimization;
            ALTER TABLE backtest_runs DROP COLUMN optimization_id;
            DROP TABLE optimization_trials;
            DROP TABLE optimizations;
            """);
    }
}
