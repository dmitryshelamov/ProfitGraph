using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610240005)]
public sealed class CreateCandleTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE candles (
                venue varchar(32) NOT NULL,
                symbol varchar(64) NOT NULL,
                timeframe varchar(8) NOT NULL,
                open_time timestamp with time zone NOT NULL,
                open numeric NOT NULL CHECK (open > 0),
                high numeric NOT NULL CHECK (high > 0),
                low numeric NOT NULL CHECK (low > 0),
                close numeric NOT NULL CHECK (close > 0),
                volume numeric NOT NULL CHECK (volume >= 0),
                source varchar(32) NOT NULL,
                imported_at timestamp with time zone NOT NULL,
                PRIMARY KEY (venue, symbol, timeframe, open_time),
                CHECK (high >= greatest(open, close) AND low <= least(open, close))
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE candles;");
    }
}
