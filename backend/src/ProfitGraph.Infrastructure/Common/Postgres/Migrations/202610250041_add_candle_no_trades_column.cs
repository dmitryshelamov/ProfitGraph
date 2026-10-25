using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Marks a candle of an open market in which nothing traded (ADR 0023, TC2): flat at the last real close with zero volume.
///     Existing rows are real candles.
/// </summary>
[Migration(202610250041)]
public sealed class AddCandleNoTradesColumnMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE candles ADD COLUMN no_trades boolean NOT NULL DEFAULT false;
            ALTER TABLE candles ADD CONSTRAINT candles_no_trades_flat
                CHECK (NOT no_trades OR (volume = 0 AND open = high AND open = low AND open = close));
            """);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE candles DROP CONSTRAINT candles_no_trades_flat; ALTER TABLE candles DROP COLUMN no_trades;");
    }
}
