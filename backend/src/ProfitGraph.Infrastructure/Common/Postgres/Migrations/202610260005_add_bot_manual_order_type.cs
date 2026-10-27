using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610260005)]
public sealed class AddBotManualOrderTypeMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE bot_manual_orders
                ADD COLUMN order_type varchar(8) NOT NULL DEFAULT 'Market',
                ADD COLUMN limit_price numeric NULL,
                ADD CONSTRAINT bot_manual_orders_limit_price CHECK ((order_type = 'Limit') = (limit_price IS NOT NULL) AND (limit_price IS NULL OR limit_price > 0));
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE bot_manual_orders
                DROP CONSTRAINT bot_manual_orders_limit_price,
                DROP COLUMN limit_price,
                DROP COLUMN order_type;
            """);
    }
}
