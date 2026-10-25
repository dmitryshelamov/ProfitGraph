using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250011)]
public sealed class CreateBotAlertTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bot_alerts (
                id uuid PRIMARY KEY,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                owner_id uuid NOT NULL REFERENCES users (id),
                kind varchar(32) NOT NULL,
                reason varchar(64) NULL,
                created_at timestamp with time zone NOT NULL,
                acknowledged_at timestamp with time zone NULL
            );
            CREATE INDEX bot_alerts_owner_created ON bot_alerts (owner_id, created_at DESC);
            CREATE INDEX bot_alerts_bot_created ON bot_alerts (bot_id, created_at DESC);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE bot_alerts;");
    }
}
