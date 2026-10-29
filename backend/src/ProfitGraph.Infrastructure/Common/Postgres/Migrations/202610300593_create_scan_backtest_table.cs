using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Scanner backtests (ADR 0059, section 6): the copied filter of a preset, the period, the background job that runs it and, once
///     complete, its result as JSON (summary, failed pairs and at most 2,000 signals).
/// </summary>
[Migration(202610300593)]
public sealed class CreateScanBacktestTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE scan_backtests (
                id uuid PRIMARY KEY,
                owner_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                venue varchar(64) NOT NULL,
                preset_name varchar(100) NOT NULL,
                filter jsonb NOT NULL,
                period_from timestamp with time zone NOT NULL,
                period_to timestamp with time zone NOT NULL,
                job_id uuid NOT NULL,
                created_at timestamp with time zone NOT NULL,
                completed_at timestamp with time zone NULL,
                result jsonb NULL
            );

            CREATE INDEX scan_backtests_owner_venue_created ON scan_backtests (owner_id, venue, created_at DESC);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE scan_backtests;");
    }
}
