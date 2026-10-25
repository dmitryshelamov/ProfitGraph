using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250012)]
public sealed class CreateBotManualOrderTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bot_manual_orders (
                id uuid PRIMARY KEY,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                side varchar(8) NOT NULL,
                amount numeric NOT NULL CHECK (amount > 0),
                status varchar(16) NOT NULL,
                engine_order_id bigint NULL,
                rejection_reason varchar(64) NULL,
                created_at timestamp with time zone NOT NULL,
                resolved_at timestamp with time zone NULL
            );
            CREATE UNIQUE INDEX bot_manual_orders_one_pending ON bot_manual_orders (bot_id) WHERE status = 'Pending';
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE bot_manual_orders;");
    }
}
