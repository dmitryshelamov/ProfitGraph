using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     The scanner snapshot keeps each pair's trend against its 50-hour EMA, the spread of its 20- and 50-hour EMAs, its hourly RSI and
///     the trend of the venue's reference pair (ADR 0071, section 3).
/// </summary>
[Migration(202610311400)]
public sealed class AddScanTrendColumnsMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE scan_snapshot_rows
                ADD COLUMN trend_ema50 numeric NULL,
                ADD COLUMN ema_spread_20x50 numeric NULL,
                ADD COLUMN rsi_1h numeric NULL,
                ADD COLUMN market_trend numeric NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE scan_snapshot_rows
                DROP COLUMN trend_ema50,
                DROP COLUMN ema_spread_20x50,
                DROP COLUMN rsi_1h,
                DROP COLUMN market_trend;
            """);
    }
}
