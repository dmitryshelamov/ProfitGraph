using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     The scanner snapshot keeps each pair's nearest support and resistance, their distances and the reward-to-risk ratio
///     (ADR 0059, section 8).
/// </summary>
[Migration(202610300594)]
public sealed class AddScanLevelColumnsMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE scan_snapshot_rows
                ADD COLUMN resistance numeric NULL,
                ADD COLUMN support numeric NULL,
                ADD COLUMN distance_to_resistance numeric NULL,
                ADD COLUMN distance_to_support numeric NULL,
                ADD COLUMN reward_risk numeric NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE scan_snapshot_rows
                DROP COLUMN resistance,
                DROP COLUMN support,
                DROP COLUMN distance_to_resistance,
                DROP COLUMN distance_to_support,
                DROP COLUMN reward_risk;
            """);
    }
}
