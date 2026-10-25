using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     A base asset may be an item named in words, as long as its symbol (ADR 0022, C1).
/// </summary>
[Migration(202610250023)]
public sealed class WidenBaseAssetColumnsMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE instrument_rules ALTER COLUMN base_asset TYPE varchar(256);
            ALTER TABLE backtest_runs ALTER COLUMN base_asset TYPE varchar(256);
            ALTER TABLE optimizations ALTER COLUMN base_asset TYPE varchar(256);
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE instrument_rules ALTER COLUMN base_asset TYPE varchar(32);
            ALTER TABLE backtest_runs ALTER COLUMN base_asset TYPE varchar(32);
            ALTER TABLE optimizations ALTER COLUMN base_asset TYPE varchar(32);
            """);
    }
}
