using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Stores the market closures a venue reports beside the cached candles (ADR 0023, TC1): half-open ranges of open times
///     without candles. Like candles they are appended and never changed.
/// </summary>
[Migration(202610250040)]
public sealed class CreateCandleClosureTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE candle_closures (
                venue varchar(32) NOT NULL,
                symbol varchar(256) NOT NULL,
                timeframe varchar(8) NOT NULL,
                from_time timestamp with time zone NOT NULL,
                to_time timestamp with time zone NOT NULL,
                source varchar(32) NOT NULL,
                recorded_at timestamp with time zone NOT NULL,
                PRIMARY KEY (venue, symbol, timeframe, from_time),
                CHECK (to_time > from_time)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE candle_closures;");
    }
}
