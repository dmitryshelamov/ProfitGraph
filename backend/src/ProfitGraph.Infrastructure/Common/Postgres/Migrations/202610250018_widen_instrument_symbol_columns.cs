using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Widens every instrument symbol column to <c>InstrumentSymbol.MaxLength</c> for venues that name instruments in
///     words (ADR 0022, C1).
/// </summary>
[Migration(202610250018)]
public sealed class WidenInstrumentSymbolColumnsMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE candles ALTER COLUMN symbol TYPE varchar(256);
            ALTER TABLE instrument_rules ALTER COLUMN symbol TYPE varchar(256);
            ALTER TABLE bots ALTER COLUMN symbol TYPE varchar(256);
            ALTER TABLE live_mark_prices ALTER COLUMN symbol TYPE varchar(256);
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE candles ALTER COLUMN symbol TYPE varchar(64);
            ALTER TABLE instrument_rules ALTER COLUMN symbol TYPE varchar(64);
            ALTER TABLE bots ALTER COLUMN symbol TYPE varchar(64);
            ALTER TABLE live_mark_prices ALTER COLUMN symbol TYPE varchar(64);
            """);
    }
}
