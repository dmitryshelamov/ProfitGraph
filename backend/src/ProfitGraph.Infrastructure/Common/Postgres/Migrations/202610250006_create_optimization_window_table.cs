using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250006)]
public sealed class CreateOptimizationWindowTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE optimizations ADD COLUMN walk_forward_summary jsonb NULL;

            ALTER TABLE optimization_trials
                ADD COLUMN window_index smallint NOT NULL DEFAULT 0 CHECK (window_index >= 0),
                DROP CONSTRAINT optimization_trials_optimization_id_trial_index_key,
                ADD CONSTRAINT optimization_trials_window_trial UNIQUE (optimization_id, window_index, trial_index);

            CREATE TABLE optimization_windows (
                optimization_id uuid NOT NULL REFERENCES optimizations (id),
                window_index smallint NOT NULL CHECK (window_index >= 0),
                train_start integer NOT NULL CHECK (train_start >= 0),
                train_count integer NOT NULL CHECK (train_count > 0),
                test_start integer NOT NULL CHECK (test_start >= 0),
                test_count integer NOT NULL CHECK (test_count > 0),
                train_from timestamp with time zone NOT NULL,
                train_to timestamp with time zone NOT NULL,
                test_from timestamp with time zone NOT NULL,
                test_to timestamp with time zone NOT NULL,
                winner_trial_index integer NULL,
                parameters jsonb NULL,
                train_metrics jsonb NULL,
                test_metrics jsonb NULL,
                status varchar(16) NOT NULL CHECK (status IN ('completed', 'no_winner', 'test_failed')),
                failure_code varchar(64) NULL,
                equity jsonb NOT NULL,
                PRIMARY KEY (optimization_id, window_index),
                CHECK ((winner_trial_index IS NULL) = (status = 'no_winner'))
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP TABLE optimization_windows;
            ALTER TABLE optimization_trials
                DROP CONSTRAINT optimization_trials_window_trial,
                ADD CONSTRAINT optimization_trials_optimization_id_trial_index_key UNIQUE (optimization_id, trial_index),
                DROP COLUMN window_index;
            ALTER TABLE optimizations DROP COLUMN walk_forward_summary;
            """);
    }
}
