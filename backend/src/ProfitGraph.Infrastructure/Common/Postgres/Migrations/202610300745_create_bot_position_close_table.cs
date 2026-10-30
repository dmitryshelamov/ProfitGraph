using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610300745)]
public sealed class CreateBotPositionCloseTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bot_position_closes (
                id uuid PRIMARY KEY,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                stop_after boolean NOT NULL,
                status varchar(16) NOT NULL CHECK (status IN ('Pending', 'Cancelling', 'Selling', 'Closed', 'Failed')),
                cancelled_orders integer NULL CHECK (cancelled_orders >= 0),
                engine_order_id bigint NULL,
                failure_reason varchar(64) NULL,
                created_at timestamp with time zone NOT NULL,
                resolved_at timestamp with time zone NULL
            );
            CREATE UNIQUE INDEX bot_position_closes_one_active ON bot_position_closes (bot_id) WHERE status IN ('Pending', 'Cancelling', 'Selling');
            CREATE INDEX bot_position_closes_latest ON bot_position_closes (bot_id, created_at DESC);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE bot_position_closes;");
    }
}
