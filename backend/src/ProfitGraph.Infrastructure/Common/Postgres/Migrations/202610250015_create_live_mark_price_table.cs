using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250015)]
public sealed class CreateLiveMarkPriceTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE live_mark_prices (
                id uuid PRIMARY KEY,
                venue varchar(64) NOT NULL,
                symbol varchar(64) NOT NULL,
                price numeric NOT NULL CHECK (price > 0),
                observed_at timestamp with time zone NOT NULL,
                UNIQUE (venue, symbol)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE live_mark_prices;");
    }
}
