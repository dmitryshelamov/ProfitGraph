using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250017)]
public sealed class CreateTelegramTablesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE telegram_links (
                user_id uuid PRIMARY KEY REFERENCES users (id) ON DELETE CASCADE,
                chat_id bigint NOT NULL UNIQUE,
                linked_at timestamp with time zone NOT NULL,
                blocked_at timestamp with time zone NULL
            );
            CREATE TABLE telegram_link_codes (
                code_hash char(64) PRIMARY KEY,
                user_id uuid NOT NULL UNIQUE REFERENCES users (id) ON DELETE CASCADE,
                expires_at timestamp with time zone NOT NULL
            );
            CREATE TABLE telegram_poller_lease (
                id boolean PRIMARY KEY DEFAULT true CHECK (id),
                holder_id uuid NOT NULL,
                expires_at timestamp with time zone NOT NULL,
                update_offset bigint NOT NULL DEFAULT 0
            );
            ALTER TABLE bot_alerts ADD COLUMN telegram_sent_at timestamp with time zone NULL;
            CREATE INDEX bot_alerts_telegram_pending ON bot_alerts (created_at) WHERE telegram_sent_at IS NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE bot_alerts DROP COLUMN telegram_sent_at;
            DROP TABLE telegram_poller_lease;
            DROP TABLE telegram_link_codes;
            DROP TABLE telegram_links;
            """);
    }
}
