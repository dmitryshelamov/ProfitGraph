using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250010)]
public sealed class CreateOutboxMessageTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE outbox_messages (
                id uuid PRIMARY KEY,
                sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
                type varchar(128) NOT NULL,
                payload jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL,
                delivered_at timestamp with time zone NULL,
                attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
                next_attempt_at timestamp with time zone NULL,
                last_error varchar(200) NULL
            );
            CREATE INDEX outbox_messages_undelivered ON outbox_messages (sequence) WHERE delivered_at IS NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE outbox_messages;");
    }
}
