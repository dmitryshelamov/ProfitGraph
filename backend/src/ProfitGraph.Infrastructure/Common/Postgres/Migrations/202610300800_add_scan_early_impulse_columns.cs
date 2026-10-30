using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     The scanner snapshot keeps each pair's 5- and 15-minute volume spikes and its breakout and width of the four hours before the
///     last fifteen minutes (ADR 0065).
/// </summary>
[Migration(202610300800)]
public sealed class AddScanEarlyImpulseColumnsMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE scan_snapshot_rows
                ADD COLUMN volume_spike_5m numeric NULL,
                ADD COLUMN volume_spike_15m numeric NULL,
                ADD COLUMN breakout_4h numeric NULL,
                ADD COLUMN range_4h numeric NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE scan_snapshot_rows
                DROP COLUMN volume_spike_5m,
                DROP COLUMN volume_spike_15m,
                DROP COLUMN breakout_4h,
                DROP COLUMN range_4h;
            """);
    }
}
