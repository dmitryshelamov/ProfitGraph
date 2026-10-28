using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610280030)]
public sealed class CreateBotOrderCancelTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bot_order_cancels (
                id uuid PRIMARY KEY,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                engine_order_id bigint NOT NULL CHECK (engine_order_id > 0),
                status varchar(16) NOT NULL,
                rejection_reason varchar(64) NULL,
                created_at timestamp with time zone NOT NULL,
                resolved_at timestamp with time zone NULL
            );
            CREATE UNIQUE INDEX bot_order_cancels_one_pending ON bot_order_cancels (bot_id) WHERE status = 'Pending';
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE bot_order_cancels;");
    }
}
