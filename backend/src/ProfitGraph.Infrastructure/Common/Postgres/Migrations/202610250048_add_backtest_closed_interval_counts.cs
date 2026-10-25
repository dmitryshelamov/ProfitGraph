using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     The grid intervals of a prepared history's warm-up and replay ranges that market closures cover (ADR 0023, TC1), so
///     a stored run or optimization restores with the same history check its attachment passed. Existing rows have none.
/// </summary>
[Migration(202610250048)]
public sealed class AddBacktestClosedIntervalCountsMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE backtest_runs
                ADD COLUMN warmup_closed_intervals integer NOT NULL DEFAULT 0 CHECK (warmup_closed_intervals >= 0),
                ADD COLUMN replay_closed_intervals integer NOT NULL DEFAULT 0 CHECK (replay_closed_intervals >= 0);
            ALTER TABLE optimizations
                ADD COLUMN warmup_closed_intervals integer NOT NULL DEFAULT 0 CHECK (warmup_closed_intervals >= 0),
                ADD COLUMN replay_closed_intervals integer NOT NULL DEFAULT 0 CHECK (replay_closed_intervals >= 0);
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE optimizations DROP COLUMN replay_closed_intervals, DROP COLUMN warmup_closed_intervals;
            ALTER TABLE backtest_runs DROP COLUMN replay_closed_intervals, DROP COLUMN warmup_closed_intervals;
            """);
    }
}
