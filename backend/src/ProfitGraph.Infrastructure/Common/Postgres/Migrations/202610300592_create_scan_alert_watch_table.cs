using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Scanner presets whose Telegram alerts are on (ADR 0059): one row per preset with the time of the last snapshot checked and the
///     pairs it remembers as a JSON object. Telegram notifications may report a preset instead of a bot.
/// </summary>
[Migration(202610300592)]
public sealed class CreateScanAlertWatchTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE scan_alert_watches (
                preset_id uuid PRIMARY KEY REFERENCES scan_presets (id) ON DELETE CASCADE,
                owner_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                enabled_at timestamp with time zone NOT NULL,
                checked_at timestamp with time zone NULL,
                pairs jsonb NOT NULL
            );

            ALTER TABLE telegram_notifications ALTER COLUMN bot_id DROP NOT NULL;
            ALTER TABLE telegram_notifications ADD COLUMN scan_preset_id uuid NULL REFERENCES scan_presets (id) ON DELETE CASCADE;
            ALTER TABLE telegram_notifications ADD CONSTRAINT telegram_notifications_subject CHECK ((bot_id IS NULL) <> (scan_preset_id IS NULL));
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DELETE FROM telegram_notifications WHERE scan_preset_id IS NOT NULL;
            ALTER TABLE telegram_notifications DROP CONSTRAINT telegram_notifications_subject;
            ALTER TABLE telegram_notifications DROP COLUMN scan_preset_id;
            ALTER TABLE telegram_notifications ALTER COLUMN bot_id SET NOT NULL;
            DROP TABLE scan_alert_watches;
            """);
    }
}
