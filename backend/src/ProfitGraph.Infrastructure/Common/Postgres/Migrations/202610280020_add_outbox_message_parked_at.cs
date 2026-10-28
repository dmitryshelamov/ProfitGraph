using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Marks an outbox message parked when its delivery failed too often, so the dispatcher stops retrying it and readiness
///     reports it (ADR 0045). The partial index keeps counting parked messages cheap; there is usually none.
/// </summary>
[Migration(202610280020)]
public sealed class AddOutboxMessageParkedAtMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE outbox_messages
                ADD COLUMN parked_at timestamp with time zone NULL,
                ADD CONSTRAINT outbox_messages_parked_undelivered CHECK (parked_at IS NULL OR delivered_at IS NULL);
            CREATE INDEX outbox_messages_parked ON outbox_messages (sequence) WHERE parked_at IS NOT NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP INDEX outbox_messages_parked;
            ALTER TABLE outbox_messages
                DROP CONSTRAINT outbox_messages_parked_undelivered,
                DROP COLUMN parked_at;
            """);
    }
}
