using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     The Telegram delivery queue (ADR 0042). Alerts not yet sent, raised in the last day after their owner's chat was linked,
///     move into it; the column that marked alerts sent goes.
/// </summary>
[Migration(202610290001)]
public sealed class CreateTelegramNotificationTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE telegram_notifications (
                id uuid PRIMARY KEY,
                owner_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                kind text NOT NULL,
                alert_id uuid NULL REFERENCES bot_alerts (id) ON DELETE CASCADE,
                details jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL,
                status text NOT NULL,
                attempts integer NOT NULL,
                next_attempt_at timestamp with time zone NOT NULL,
                claim_id uuid NULL,
                claimed_until timestamp with time zone NULL,
                sent_at timestamp with time zone NULL,
                last_error text NULL
            );
            CREATE INDEX telegram_notifications_due ON telegram_notifications (next_attempt_at, created_at) WHERE status = 'Pending';
            CREATE INDEX telegram_notifications_finished ON telegram_notifications (created_at) WHERE status <> 'Pending';
            INSERT INTO telegram_notifications (id, owner_id, bot_id, kind, alert_id, details, created_at, status, attempts, next_attempt_at)
            SELECT gen_random_uuid(), alert.owner_id, alert.bot_id, 'Alert', alert.id,
                CASE WHEN alert.reason IS NULL THEN jsonb_build_object('kind', alert.kind)
                     ELSE jsonb_build_object('kind', alert.kind, 'reason', alert.reason) END,
                alert.created_at, 'Pending', 0, alert.created_at
            FROM bot_alerts AS alert
            JOIN telegram_links AS link ON link.user_id = alert.owner_id
            WHERE alert.telegram_sent_at IS NULL AND link.blocked_at IS NULL AND alert.created_at >= link.linked_at
                AND alert.created_at >= now() - interval '24 hours';
            DROP INDEX bot_alerts_telegram_pending;
            ALTER TABLE bot_alerts DROP COLUMN telegram_sent_at;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE bot_alerts ADD COLUMN telegram_sent_at timestamp with time zone NULL;
            CREATE INDEX bot_alerts_telegram_pending ON bot_alerts (created_at) WHERE telegram_sent_at IS NULL;
            DROP TABLE telegram_notifications;
            """);
    }
}
