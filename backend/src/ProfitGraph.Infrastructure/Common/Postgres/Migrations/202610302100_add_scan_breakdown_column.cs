using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     The scanner snapshot keeps each pair's distance to the low of the range before the last fifteen minutes (ADR 0068).
/// </summary>
[Migration(202610302100)]
public sealed class AddScanBreakdownColumnMigration : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE scan_snapshot_rows ADD COLUMN breakdown_4h numeric NULL;");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE scan_snapshot_rows DROP COLUMN breakdown_4h;");
    }
}
