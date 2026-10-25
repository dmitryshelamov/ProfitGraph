using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250014)]
public sealed class CreateBotEventTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bot_events (
                id uuid PRIMARY KEY,
                sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                kind varchar(32) NOT NULL,
                details jsonb NOT NULL,
                occurred_at timestamp with time zone NOT NULL
            );
            CREATE INDEX bot_events_bot_sequence ON bot_events (bot_id, sequence DESC);
            CREATE INDEX bot_events_occurred ON bot_events (occurred_at);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE bot_events;");
    }
}
