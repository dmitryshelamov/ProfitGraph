using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     A submit the bot accepted but the core holds until a condition holds (ADR 0024): the new status and the reason of the
///     wait. Its next check is the intent's <c>next_attempt_at</c>. Existing intents have no deferral.
/// </summary>
[Migration(202610260002)]
public sealed class AddOrderIntentDeferralMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE bot_order_intents
                DROP CONSTRAINT bot_order_intents_status_check,
                ADD CONSTRAINT bot_order_intents_status_check CHECK (status IN ('Pending', 'Deferred', 'Sending', 'Sent', 'Failed', 'Rejected')),
                ADD COLUMN deferral_code varchar(32) NULL,
                ADD COLUMN deferral_since timestamp with time zone NULL,
                ADD COLUMN deferral_until timestamp with time zone NULL,
                ADD COLUMN deferral_low numeric NULL,
                ADD COLUMN deferral_high numeric NULL,
                ADD CONSTRAINT bot_order_intents_deferred_has_reason CHECK (status <> 'Deferred' OR (deferral_code IS NOT NULL AND deferral_since IS NOT NULL AND next_attempt_at IS NOT NULL));
            DROP INDEX bot_order_intents_due;
            CREATE INDEX bot_order_intents_due ON bot_order_intents (status, next_attempt_at, sequence)
                WHERE status IN ('Pending', 'Deferred', 'Sending');
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DELETE FROM bot_order_intents WHERE status = 'Deferred';
            DROP INDEX bot_order_intents_due;
            CREATE INDEX bot_order_intents_due ON bot_order_intents (status, next_attempt_at, sequence)
                WHERE status IN ('Pending', 'Sending');
            ALTER TABLE bot_order_intents
                DROP CONSTRAINT bot_order_intents_deferred_has_reason,
                DROP COLUMN deferral_high,
                DROP COLUMN deferral_low,
                DROP COLUMN deferral_until,
                DROP COLUMN deferral_since,
                DROP COLUMN deferral_code,
                DROP CONSTRAINT bot_order_intents_status_check,
                ADD CONSTRAINT bot_order_intents_status_check CHECK (status IN ('Pending', 'Sending', 'Sent', 'Failed', 'Rejected'));
            """);
    }
}
